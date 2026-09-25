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

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string SettingsPath => Path.Combine(_dir, "settings.json");
    private string LegacyPath => Path.Combine(_dir, "legacy", "settings.json");
    private string BadBackupPath => SettingsPath + ".bad-20260304050607";

    private SettingsStore CreateStore() =>
        new(SettingsPath, LegacyPath, _clock, new AppLog(Path.Combine(_dir, "app.log"), _clock));

    private void WriteSettings(string json) => File.WriteAllText(SettingsPath, json);

    private static JsonElement ReadJson(string path) => JsonDocument.Parse(File.ReadAllText(path)).RootElement;

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
}
