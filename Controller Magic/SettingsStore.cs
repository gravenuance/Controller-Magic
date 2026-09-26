using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ControllerMagic;

// What a settings file turned out to be, which decides what Load may write back.
internal enum SettingsFileState
{
    Current,
    Older,
    Newer,
    Unreadable,
}

internal sealed record ParsedSettings(
    AppSettings Settings,
    SettingsFileState State,
    int FileVersion,
    IReadOnlyList<string> DroppedFields,
    IReadOnlyList<string> CorrectedFields);

// Reads, migrates and writes settings.json. Nothing the user saved is ever discarded without a
// copy on disk first, and a file from a newer build is never written over.
internal sealed class SettingsStore : IDisposable
{
    // Slider drags change a value many times a second; RequestSave writes once after this quiet gap.
    internal static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,

        // JsonObject throws ArgumentException on a duplicate key when first read; reject it at parse.
        AllowDuplicateProperties = false,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // Index N upgrades a version-N tree to N+1, on the raw JSON so renamed or reshaped fields
    // can still be read; version 0 (no SchemaVersion key) to 1 needed no change, 1 to 2 undoes 1.9.0's damage.
    internal static IReadOnlyList<Action<JsonObject>> MigrationSteps { get; } =
    [
        _ => { },
        RestoreFieldsZeroedBy190,
    ];

    private static readonly string[] FieldsZeroedBy190 =
    [
        nameof(AppSettings.StickDeadZone),
        nameof(AppSettings.ScrollDeadZone),
        nameof(AppSettings.KeyboardDeadZone),
        nameof(AppSettings.TouchpadSpeed),
    ];

    // 1.9.0 validated before its ranges existed and saved these as 0; a touchpad speed of 0 is
    // below the slider's minimum, so all four at 0 can only be that damage. Removed fields bind to defaults.
    private static void RestoreFieldsZeroedBy190(JsonObject root)
    {
        bool allZero = Array.TrueForAll(FieldsZeroedBy190, name =>
            root[name] is JsonValue value && value.TryGetValue(out int number) && number == 0);
        if (!allZero)
            return;

        foreach (string name in FieldsZeroedBy190)
            root.Remove(name);
    }

    private readonly string _path;
    private readonly string? _legacyPath;
    private readonly TimeProvider _clock;
    private readonly AppLog _log;

    // Save() runs from the UI thread and from background continuations; without this two callers
    // race on the same temp file.
    private readonly Lock _saveLock = new();
    private bool _readOnlyLogged;

    private readonly Lock _pendingLock = new();
    private AppSettings? _pendingSave;
    private ITimer? _saveTimer;
    private bool _disposed;

    internal SettingsStore(string path, string? legacyPath, TimeProvider clock, AppLog log)
    {
        _path = path;
        _legacyPath = legacyPath;
        _clock = clock;
        _log = log;
    }

    // %LocalAppData%, not next to the exe: installs under Program Files aren't writable by a
    // standard user. The old next-to-the-exe file is picked up once as the legacy path.
    internal static SettingsStore CreateDefault() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerMagic", "settings.json"),
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json"),
        TimeProvider.System,
        AppLog.Default);

    // Settable only so the test run can point AppSettings.Instance away from the user's real file.
    internal static SettingsStore Default { get; set; } = CreateDefault();

    // True when the file on disk must be left alone for this session, e.g. it's from a newer build.
    internal bool IsReadOnly { get; private set; }

    internal AppSettings Load()
    {
        if (File.Exists(_path))
            return LoadFrom(_path, isLegacy: false);

        if (_legacyPath != null && File.Exists(_legacyPath))
            return LoadFrom(_legacyPath, isLegacy: true);

        return Attach(AppSettings.CreateDefault());
    }

    private AppSettings LoadFrom(string source, bool isLegacy)
    {
        string json;
        try
        {
            json = File.ReadAllText(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning($"Settings: couldn't read {source}; using defaults without saving", ex);
            IsReadOnly = true;
            return Attach(AppSettings.CreateDefault());
        }

        var parsed = Parse(json);
        var settings = Attach(parsed.Settings);

        if (parsed.State == SettingsFileState.Newer)
        {
            _log.Warning(
                $"Settings: {source} is schema version {parsed.FileVersion}, newer than this build " +
                $"understands ({AppSettings.CurrentSchemaVersion}); using defaults and leaving the file untouched");
            IsReadOnly = true;
            return settings;
        }

        // A clamped value is written back so it's corrected once, not on every launch; the original is kept.
        bool lostData = parsed.State == SettingsFileState.Unreadable
            || parsed.DroppedFields.Count > 0
            || parsed.CorrectedFields.Count > 0;
        if (lostData && !HasBadCopyOf(json) && !TryBackUp(source, BadCopyPath()))
            return settings;
        if (parsed.State == SettingsFileState.Older && !TryBackUp(source, $"{_path}.v{parsed.FileVersion}.bak"))
            return settings;

        if (parsed.State == SettingsFileState.Unreadable)
            _log.Warning($"Settings: {source} couldn't be read as settings; using defaults");
        else if (parsed.DroppedFields.Count > 0)
            _log.Warning($"Settings: {source} had unusable values for {string.Join(", ", parsed.DroppedFields)}; using defaults for those");

        if (parsed.CorrectedFields.Count > 0)
            _log.Warning($"Settings: {source} had out-of-range values for {string.Join(", ", parsed.CorrectedFields)}; corrected them");

        bool needsWrite = isLegacy || lostData || parsed.State == SettingsFileState.Older;
        if (needsWrite && Save(settings) && isLegacy)
            TryDeleteLegacyFile(source);

        return settings;
    }

    // Damage seen again (its correction couldn't be saved) is already backed up; one copy is enough.
    // A failed comparison only costs one extra copy.
    private bool HasBadCopyOf(string json)
    {
        string dir = Path.GetDirectoryName(_path)!;
        if (!Directory.Exists(dir))
            return false;

        try
        {
            return Directory.EnumerateFiles(dir, $"{Path.GetFileName(_path)}.bad-*")
                .Any(copy => File.ReadAllText(copy) == json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning($"Settings: couldn't compare {_path} with its earlier copies; copying it again", ex);
            return false;
        }
    }

    private string BadCopyPath() =>
        $"{_path}.bad-{_clock.GetUtcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}";

    // A copy that can't be made leaves the store read-only, so the original is never lost.
    private bool TryBackUp(string source, string destination)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            _log.Info($"Settings: copied {source} to {destination}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning($"Settings: couldn't copy {source} to {destination}; changes won't be saved this session", ex);
            IsReadOnly = true;
            return false;
        }
    }

    private AppSettings Attach(AppSettings settings)
    {
        settings.Store = this;
        return settings;
    }

    // Pure: raw JSON to settings plus what happened on the way, so every path is testable without disk.
    internal static ParsedSettings Parse(string json)
    {
        JsonObject? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: ReadOptions) as JsonObject;
        }
        catch (JsonException)
        {
            root = null;
        }

        if (root == null || !TryReadSchemaVersion(root, out int version))
            return new(AppSettings.CreateDefault(), SettingsFileState.Unreadable, 0, [], []);

        if (version > AppSettings.CurrentSchemaVersion)
        {
            // The newer build already set startup up; redoing it here would re-enable it.
            var defaults = AppSettings.CreateDefault();
            defaults.HasInitializedStartup = true;
            return new(defaults, SettingsFileState.Newer, version, [], []);
        }

        for (int step = version; step < AppSettings.CurrentSchemaVersion; step++)
            MigrationSteps[step](root);
        root[nameof(AppSettings.SchemaVersion)] = AppSettings.CurrentSchemaVersion;

        var dropped = new List<string>();
        var settings = Bind(root, dropped);
        if (settings == null)
            return new(AppSettings.CreateDefault(), SettingsFileState.Unreadable, version, [], []);

        var corrected = settings.Validate();
        var state = version < AppSettings.CurrentSchemaVersion ? SettingsFileState.Older : SettingsFileState.Current;
        return new(settings, state, version, dropped, corrected);
    }

    private static bool TryReadSchemaVersion(JsonObject root, out int version)
    {
        version = 0;
        if (!root.TryGetPropertyValue(nameof(AppSettings.SchemaVersion), out var node))
            return true;

        return node is JsonValue value && value.TryGetValue(out version) && version >= 0;
    }

    // A wrong-typed field costs only that field: it's removed and binding retried.
    private static AppSettings? Bind(JsonObject root, List<string> dropped)
    {
        while (true)
        {
            try
            {
                return root.Deserialize<AppSettings>();
            }
            catch (JsonException ex) when (TopLevelProperty(ex.Path) is { } name && root.Remove(name))
            {
                dropped.Add(name);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    // "$.Name", "$.Name[2]" or "$['Name']" to "Name".
    internal static string? TopLevelProperty(string? jsonPath)
    {
        if (jsonPath == null)
            return null;

        if (jsonPath.StartsWith("$['", StringComparison.Ordinal))
        {
            int close = jsonPath.IndexOf("']", 3, StringComparison.Ordinal);
            return close > 3 ? jsonPath[3..close] : null;
        }

        if (!jsonPath.StartsWith("$.", StringComparison.Ordinal))
            return null;

        int end = jsonPath.IndexOfAny(['.', '['], 2);
        string name = end < 0 ? jsonPath[2..] : jsonPath[2..end];
        return name.Length > 0 ? name : null;
    }

    internal bool Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_saveLock)
        {
            if (IsReadOnly)
            {
                if (!_readOnlyLogged)
                    _log.Warning($"Settings: not saving over {_path} this session; changes stay in memory");
                _readOnlyLogged = true;
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
                string json = JsonSerializer.Serialize(settings, WriteOptions);

                // Temp file plus same-volume move, so a crash mid-write never leaves a truncated file.
                string tempPath = _path + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _path, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Warning($"Settings: failed to save {_path}", ex);
                return false;
            }
        }
    }

    // Lives here rather than in a form, so a save requested after the form has closed still lands.
    internal void RequestSave(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_pendingLock)
        {
            if (_disposed)
            {
                _pendingSave = null;
                Save(settings);
                return;
            }

            _pendingSave = settings;
            _saveTimer ??= _clock.CreateTimer(_ => FlushPendingSave(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    internal void FlushPendingSave()
    {
        AppSettings? pending;
        lock (_pendingLock)
        {
            pending = _pendingSave;
            _pendingSave = null;
            _saveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        if (pending != null)
            Save(pending);
    }

    public void Dispose()
    {
        lock (_pendingLock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        FlushPendingSave();
        _saveTimer?.Dispose();
    }

    private void TryDeleteLegacyFile(string legacyPath)
    {
        try
        {
            File.Delete(legacyPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warning($"Settings: failed to remove legacy settings file {legacyPath}", ex);
        }
    }
}
