using System.Text.Json;
using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class AppSettingsTests
{
    [Fact]
    public void ResolveLoaded_NullInput_ReturnsDefaultsAtCurrentSchemaVersion()
    {
        var resolved = AppSettings.ResolveLoaded(null);

        Assert.Equal(AppSettings.CurrentSchemaVersion, resolved.SchemaVersion);
    }

    [Fact]
    public void ResolveLoaded_PreVersioningFile_MigratesToCurrentSchemaVersion()
    {
        // A file written before SchemaVersion existed deserializes with SchemaVersion 0 (the C#
        // default for a JSON payload missing that key entirely) and its other fields intact.
        var legacy = new AppSettings { SchemaVersion = 0, StickSensitivity = 0.042f };

        var resolved = AppSettings.ResolveLoaded(legacy);

        Assert.Equal(AppSettings.CurrentSchemaVersion, resolved.SchemaVersion);
        Assert.Equal(0.042f, resolved.StickSensitivity);
        Assert.Same(legacy, resolved);
    }

    [Fact]
    public void ResolveLoaded_CurrentSchemaVersion_ReturnsSameInstanceUnchanged()
    {
        var current = new AppSettings { SchemaVersion = AppSettings.CurrentSchemaVersion, StickSensitivity = 0.05f };

        var resolved = AppSettings.ResolveLoaded(current);

        Assert.Same(current, resolved);
        Assert.Equal(0.05f, resolved.StickSensitivity);
    }

    [Fact]
    public void ResolveLoaded_NewerThanCurrentSchemaVersion_FallsBackToDefaultsInstead()
    {
        // A file from a future version of the app might have fields shaped in ways this build
        // doesn't understand - use defaults rather than risk misinterpreting it.
        var future = new AppSettings { SchemaVersion = AppSettings.CurrentSchemaVersion + 1, StickSensitivity = 0.999f };

        var resolved = AppSettings.ResolveLoaded(future);

        Assert.Equal(AppSettings.CurrentSchemaVersion, resolved.SchemaVersion);
        Assert.NotSame(future, resolved);
        Assert.NotEqual(0.999f, resolved.StickSensitivity);
    }

    [Fact]
    public void LoadFrom_MissingSchemaVersionKey_DeserializesAsZero()
    {
        // Exactly what a real pre-versioning settings.json on disk looks like: no "SchemaVersion"
        // key at all.
        string path = Path.Combine(Path.GetTempPath(), $"cm-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"StickSensitivity": 0.077}""");

        try
        {
            var loaded = AppSettings.LoadFrom(path);

            Assert.NotNull(loaded);
            Assert.Equal(0, loaded.SchemaVersion);
            Assert.Equal(0.077f, loaded.StickSensitivity);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFrom_CorruptJson_ReturnsNullInsteadOfThrowing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cm-settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ this is not valid json");

        try
        {
            var loaded = AppSettings.LoadFrom(path);

            Assert.Null(loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFrom_NonexistentPath_ReturnsNullInsteadOfThrowing()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cm-settings-{Guid.NewGuid():N}-does-not-exist.json");

        var loaded = AppSettings.LoadFrom(path);

        Assert.Null(loaded);
    }

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [Fact]
    public void RoundTrip_SerializeThenLoadFrom_PreservesFields()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cm-settings-{Guid.NewGuid():N}.json");
        var original = new AppSettings
        {
            StickDeadZone = 1234,
            WatchedProcessNames = ["notepad", "steam"]
        };

        try
        {
            string json = JsonSerializer.Serialize(original, IndentedJson);
            File.WriteAllText(path, json);

            var loaded = AppSettings.LoadFrom(path);

            Assert.NotNull(loaded);
            Assert.Equal(1234, loaded.StickDeadZone);
            Assert.Equal(["notepad", "steam"], loaded.WatchedProcessNames);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
