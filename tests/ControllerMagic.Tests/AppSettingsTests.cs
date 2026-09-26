using System.Text.Json;
using ControllerMagic;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ControllerMagic.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-03-04T05:06:07Z", System.Globalization.CultureInfo.InvariantCulture);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cm-settings-{Guid.NewGuid():N}");
    private readonly FakeTimeProvider _clock = new(Now);

    public AppSettingsTests() => Directory.CreateDirectory(_dir);

    private readonly List<SettingsStore> _stores = [];

    public void Dispose()
    {
        foreach (var store in _stores)
            store.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private string SettingsPath => Path.Combine(_dir, "settings.json");
    private string LegacyPath => Path.Combine(_dir, "legacy", "settings.json");
    private string BadBackupPath => SettingsPath + ".bad-20260304050607";

    private SettingsStore CreateStore()
    {
        var store = new SettingsStore(SettingsPath, LegacyPath, _clock, new AppLog(Path.Combine(_dir, "app.log"), _clock));
        _stores.Add(store);
        return store;
    }

    private void WriteSettings(string json) => File.WriteAllText(SettingsPath, json);

    private static JsonElement ReadJson(string path) => JsonDocument.Parse(File.ReadAllText(path)).RootElement;

    // Regression: Instance loaded before the ranges were initialised, so every range read as 0..0.
    [Fact]
    public void Instance_KeepsSavedInRangeValues()
    {
        var settings = AppSettings.Instance;

        Assert.Equal(TestEnvironment.StickDeadZone, settings.StickDeadZone);
        Assert.Equal(TestEnvironment.ScrollDeadZone, settings.ScrollDeadZone);
        Assert.Equal(TestEnvironment.KeyboardDeadZone, settings.KeyboardDeadZone);
        Assert.Equal(TestEnvironment.TouchpadSpeed, settings.TouchpadSpeed);
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaultsWithoutWriting()
    {
        var settings = CreateStore().Load();

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(4000, settings.StickDeadZone);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Load_CurrentFile_KeepsFieldsAndLeavesFileAlone()
    {
        string json = $$"""{"SchemaVersion": {{AppSettings.CurrentSchemaVersion}}, "StickDeadZone": 1234, "WatchedProcessNames": ["notepad"]}""";
        WriteSettings(json);

        var settings = CreateStore().Load();

        Assert.Equal(1234, settings.StickDeadZone);
        Assert.Equal(["notepad"], settings.WatchedProcessNames);
        Assert.Equal(json, File.ReadAllText(SettingsPath));
        Assert.Single(Directory.GetFiles(_dir), SettingsPath);
    }

    [Fact]
    public void Load_CorruptJson_BacksUpFileBeforeFallingBackToDefaults()
    {
        const string corrupt = "{ this is not valid json";
        WriteSettings(corrupt);

        var settings = CreateStore().Load();

        Assert.Equal(4000, settings.StickDeadZone);
        Assert.Equal(corrupt, File.ReadAllText(BadBackupPath));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"SchemaVersion": "one"}""")]
    [InlineData("""{"SchemaVersion": -1}""")]
    [InlineData("""{"StickDeadZone": 1, "StickDeadZone": 2}""")]
    [InlineData("""{"WatchedProcessNames": {"a": 1, "a": 2}}""")]
    public void Load_UnusableShape_BacksUpFile(string json)
    {
        WriteSettings(json);

        var settings = CreateStore().Load();

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(json, File.ReadAllText(BadBackupPath));
    }

    [Fact]
    public void Load_OneWrongTypedField_KeepsTheOtherFieldsAndBacksUpTheFile()
    {
        string json = $$"""{"SchemaVersion": {{AppSettings.CurrentSchemaVersion}}, "StickDeadZone": "lots", "TouchpadSpeed": 900}""";
        WriteSettings(json);

        var settings = CreateStore().Load();

        Assert.Equal(4000, settings.StickDeadZone);
        Assert.Equal(900, settings.TouchpadSpeed);
        Assert.Equal(json, File.ReadAllText(BadBackupPath));
        Assert.Equal(900, ReadJson(SettingsPath).GetProperty("TouchpadSpeed").GetInt32());
    }

    [Fact]
    public void Load_OutOfRangeValue_SavesTheCorrectionOnceSoTheNextLoadIsQuiet()
    {
        string json = $$"""{"SchemaVersion": {{AppSettings.CurrentSchemaVersion}}, "StickDeadZone": 99999, "TouchpadSpeed": 900}""";
        WriteSettings(json);
        string logPath = Path.Combine(_dir, "app.log");

        CreateStore().Load();
        int warningsAfterFirstLoad = File.ReadAllLines(logPath).Count(line => line.Contains("out-of-range", StringComparison.Ordinal));
        var reloaded = CreateStore().Load();
        int warningsAfterSecondLoad = File.ReadAllLines(logPath).Count(line => line.Contains("out-of-range", StringComparison.Ordinal));

        Assert.Equal(1, warningsAfterFirstLoad);
        Assert.Equal(1, warningsAfterSecondLoad);
        Assert.Equal(AppSettings.StickDeadZoneRange.Max, ReadJson(SettingsPath).GetProperty("StickDeadZone").GetInt32());
        Assert.Equal(900, reloaded.TouchpadSpeed);
        Assert.Equal(json, File.ReadAllText(BadBackupPath));
    }

    // Regression: when the correction couldn't be saved, every launch added another identical copy.
    [Fact]
    public void Load_SameDamageAgain_KeepsOneCopy()
    {
        string json = $$"""{"SchemaVersion": {{AppSettings.CurrentSchemaVersion}}, "StickDeadZone": 99999}""";
        WriteSettings(json);
        CreateStore().Load();
        WriteSettings(json);
        _clock.Advance(TimeSpan.FromMinutes(1));

        CreateStore().Load();

        Assert.Equal([BadBackupPath], Directory.GetFiles(_dir, "settings.json.bad-*"));
    }

    [Fact]
    public void Load_DifferentDamage_KeepsACopyOfEach()
    {
        WriteSettings($$"""{"SchemaVersion": {{AppSettings.CurrentSchemaVersion}}, "StickDeadZone": 99999}""");
        CreateStore().Load();
        WriteSettings($$"""{"SchemaVersion": {{AppSettings.CurrentSchemaVersion}}, "StickDeadZone": 88888}""");
        _clock.Advance(TimeSpan.FromMinutes(1));

        CreateStore().Load();

        Assert.Equal(2, Directory.GetFiles(_dir, "settings.json.bad-*").Length);
    }

    [Fact]
    public void Load_OutOfRangeValueWhenBackupFails_LeavesTheFileUntouched()
    {
        string json = $$"""{"SchemaVersion": {{AppSettings.CurrentSchemaVersion}}, "StickDeadZone": 99999}""";
        WriteSettings(json);
        Directory.CreateDirectory(BadBackupPath);
        var store = CreateStore();

        var settings = store.Load();

        Assert.Equal(AppSettings.StickDeadZoneRange.Max, settings.StickDeadZone);
        Assert.True(store.IsReadOnly);
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Load_PreVersioningFile_MigratesAndKeepsACopyOfTheOriginal()
    {
        const string legacy = """{"StickSensitivity": 0.042}""";
        WriteSettings(legacy);

        var settings = CreateStore().Load();

        Assert.Equal(0.042f, settings.StickSensitivity);
        Assert.Equal(legacy, File.ReadAllText(SettingsPath + ".v0.bak"));
        Assert.Equal(AppSettings.CurrentSchemaVersion, ReadJson(SettingsPath).GetProperty("SchemaVersion").GetInt32());
    }

    [Fact]
    public void Load_NewerSchema_RunsOnDefaultsAndNeverWritesTheFile()
    {
        string future = $$"""{"SchemaVersion": {{AppSettings.CurrentSchemaVersion + 1}}, "StickDeadZone": 777}""";
        WriteSettings(future);
        var store = CreateStore();

        var settings = store.Load();
        int loadedDeadZone = settings.StickDeadZone;
        settings.StickDeadZone = 55;
        settings.Save();

        Assert.Equal(4000, loadedDeadZone);
        Assert.True(store.IsReadOnly);
        Assert.Equal(future, File.ReadAllText(SettingsPath));
        Assert.Single(Directory.GetFiles(_dir, "settings.json*"));
    }

    [Fact]
    public void Load_NewerSchema_TreatsStartupAsAlreadyInitialized()
    {
        WriteSettings($$"""{"SchemaVersion": {{AppSettings.CurrentSchemaVersion + 1}}}""");

        var settings = CreateStore().Load();

        Assert.True(settings.HasInitializedStartup);
    }

    [Fact]
    public void Load_LegacyFile_MovesItToTheNewLocation()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LegacyPath)!);
        File.WriteAllText(LegacyPath, """{"StickDeadZone": 2222}""");

        var settings = CreateStore().Load();

        Assert.Equal(2222, settings.StickDeadZone);
        Assert.Equal(2222, ReadJson(SettingsPath).GetProperty("StickDeadZone").GetInt32());
        Assert.False(File.Exists(LegacyPath));
    }

    [Fact]
    public void Load_UnreadableLegacyFile_IsBackedUpBeforeBeingRemoved()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LegacyPath)!);
        File.WriteAllText(LegacyPath, "not json");

        CreateStore().Load();

        Assert.Equal("not json", File.ReadAllText(BadBackupPath));
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsFields()
    {
        var settings = CreateStore().Load();
        settings.StickDeadZone = 1234;
        settings.WatchedProcessNames = ["notepad", "steam"];
        settings.Save();

        var reloaded = CreateStore().Load();

        Assert.Equal(1234, reloaded.StickDeadZone);
        Assert.Equal(["notepad", "steam"], reloaded.WatchedProcessNames);
    }

    [Fact]
    public void Parse_EveryOlderSchemaVersion_MigratesToCurrent()
    {
        for (int version = 0; version < AppSettings.CurrentSchemaVersion; version++)
        {
            var parsed = SettingsStore.Parse($$"""{"SchemaVersion": {{version}}}""");

            Assert.Equal(SettingsFileState.Older, parsed.State);
            Assert.Equal(AppSettings.CurrentSchemaVersion, parsed.Settings.SchemaVersion);
        }
    }

    private const string ZeroedBy190 =
        """{"SchemaVersion": 1, "StickDeadZone": 0, "ScrollDeadZone": 0, "KeyboardDeadZone": 0, "TouchpadSpeed": 0, "StickSensitivity": 0.03, "WatchedProcessNames": ["mpv"]}""";

    [Fact]
    public void Parse_Version1ZeroedBy190_RestoresThoseFieldsToDefaults()
    {
        var parsed = SettingsStore.Parse(ZeroedBy190);
        var defaults = new AppSettings();

        Assert.Equal(defaults.StickDeadZone, parsed.Settings.StickDeadZone);
        Assert.Equal(defaults.ScrollDeadZone, parsed.Settings.ScrollDeadZone);
        Assert.Equal(defaults.KeyboardDeadZone, parsed.Settings.KeyboardDeadZone);
        Assert.Equal(defaults.TouchpadSpeed, parsed.Settings.TouchpadSpeed);
        Assert.Empty(parsed.CorrectedFields);
    }

    [Fact]
    public void Parse_Version1ZeroedBy190_KeepsTheOtherFields()
    {
        var parsed = SettingsStore.Parse(ZeroedBy190);

        Assert.Equal(0.03f, parsed.Settings.StickSensitivity);
        Assert.Equal(["mpv"], parsed.Settings.WatchedProcessNames);
    }

    // Deadzones of 0 are a legal choice; only the touchpad speed of 0 the Settings slider can't produce marks the damage.
    [Fact]
    public void Parse_Version1ZeroDeadZonesWithRealTouchpadSpeed_KeepsThem()
    {
        var parsed = SettingsStore.Parse(
            """{"SchemaVersion": 1, "StickDeadZone": 0, "ScrollDeadZone": 0, "KeyboardDeadZone": 0, "TouchpadSpeed": 900}""");

        Assert.Equal(0, parsed.Settings.StickDeadZone);
        Assert.Equal(0, parsed.Settings.ScrollDeadZone);
        Assert.Equal(0, parsed.Settings.KeyboardDeadZone);
        Assert.Equal(900, parsed.Settings.TouchpadSpeed);
    }

    [Fact]
    public void Load_Version1ZeroedBy190_WritesTheRepairBackKeepingABackup()
    {
        WriteSettings(ZeroedBy190);

        CreateStore().Load();

        Assert.Equal(ZeroedBy190, File.ReadAllText(SettingsPath + ".v1.bak"));
        var saved = ReadJson(SettingsPath);
        Assert.Equal(AppSettings.CurrentSchemaVersion, saved.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal(new AppSettings().ScrollDeadZone, saved.GetProperty("ScrollDeadZone").GetInt32());
    }

    [Fact]
    public void Parse_NullLists_FallBackToDefaultLists()
    {
        var parsed = SettingsStore.Parse("""{"WatchedProcessNames": null, "StreamingServiceNames": null}""");

        Assert.Equal(new AppSettings().WatchedProcessNames, parsed.Settings.WatchedProcessNames);
        Assert.Equal(new AppSettings().StreamingServiceNames, parsed.Settings.StreamingServiceNames);
        Assert.Equal(["WatchedProcessNames", "StreamingServiceNames"], parsed.CorrectedFields);
    }

    [Fact]
    public void Parse_BlankListEntries_AreRemoved()
    {
        var parsed = SettingsStore.Parse("""{"WatchedProcessNames": ["vlc", null, "  "]}""");

        Assert.Equal(["vlc"], parsed.Settings.WatchedProcessNames);
        Assert.Equal(["WatchedProcessNames"], parsed.CorrectedFields);
    }

    [Theory]
    [InlineData("StickDeadZone", "-5", 0f)]
    [InlineData("StickDeadZone", "99999", 10000f)]
    [InlineData("ScrollDeadZone", "-1", 0f)]
    [InlineData("KeyboardDeadZone", "20000", 10000f)]
    [InlineData("TouchpadSpeed", "0", 300f)]
    [InlineData("TouchpadSpeed", "100000", 3000f)]
    [InlineData("StickSensitivity", "0", 0.005f)]
    [InlineData("StickSensitivity", "4", 0.06f)]
    [InlineData("StickAccelPower", "-3", 1f)]
    [InlineData("StickAccelPower", "0", 1f)]
    [InlineData("StickAccelPower", "9", 4f)]
    [InlineData("StickRampSeconds", "-0.5", 0f)]
    [InlineData("StickRampSeconds", "60", 1f)]
    public void Parse_OutOfRangeNumber_IsClampedToTheSliderRange(string field, string value, float expected)
    {
        var parsed = SettingsStore.Parse($$"""{"{{field}}": {{value}}}""");

        var property = typeof(AppSettings).GetProperty(field)!;
        Assert.Equal(expected, Convert.ToSingle(property.GetValue(parsed.Settings), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal([field], parsed.CorrectedFields);
    }

    [Fact]
    public void Parse_InRangeValues_AreLeftAsIs()
    {
        var parsed = SettingsStore.Parse("""{"StickDeadZone": 0, "TouchpadSpeed": 3000, "StickSensitivity": 0.0185, "StickRampSeconds": 0}""");

        Assert.Equal(0, parsed.Settings.StickDeadZone);
        Assert.Equal(3000, parsed.Settings.TouchpadSpeed);
        Assert.Equal(0.0185f, parsed.Settings.StickSensitivity);
        Assert.Empty(parsed.CorrectedFields);
    }

    [Fact]
    public void Defaults_AreAllWithinTheirRanges()
    {
        Assert.Empty(AppSettings.CreateDefault().Validate());
    }

    [Fact]
    public void RequestSave_WritesOnlyAfterAQuietGap()
    {
        var settings = CreateStore().Load();

        settings.StickDeadZone = 1111;
        settings.RequestSave();
        _clock.Advance(TimeSpan.FromMilliseconds(300));
        settings.StickDeadZone = 2222;
        settings.RequestSave();
        _clock.Advance(TimeSpan.FromMilliseconds(300));
        bool writtenEarly = File.Exists(SettingsPath);
        _clock.Advance(TimeSpan.FromMilliseconds(100));

        Assert.False(writtenEarly);
        Assert.Equal(2222, ReadJson(SettingsPath).GetProperty("StickDeadZone").GetInt32());
    }

    [Fact]
    public void FlushPendingSave_WritesARequestedSaveImmediately()
    {
        var settings = CreateStore().Load();
        settings.StickDeadZone = 3333;
        settings.RequestSave();

        settings.FlushPendingSave();

        Assert.Equal(3333, ReadJson(SettingsPath).GetProperty("StickDeadZone").GetInt32());
    }

    [Fact]
    public void FlushPendingSave_WithNothingRequested_WritesNothing()
    {
        var settings = CreateStore().Load();

        settings.FlushPendingSave();

        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Dispose_FlushesAPendingSave()
    {
        var store = CreateStore();
        var settings = store.Load();
        settings.StickDeadZone = 4444;
        settings.RequestSave();

        store.Dispose();

        Assert.Equal(4444, ReadJson(SettingsPath).GetProperty("StickDeadZone").GetInt32());
    }
}
