namespace ControllerMagic;

// A folder plus the reason an environment override was ignored, so the caller can log it.
internal readonly record struct ResolvedDirectory(string Path, string? Warning = null);

// Where the app keeps what it can rebuild (log, downloaded drivers). Settings stay in
// %LocalAppData%\ControllerMagic and are not covered here.
internal static class AppPaths
{
    internal const string CacheDirVariable = "CONTROLLERMAGIC_CACHE_DIR";
    private const string AppFolderName = "ControllerMagic";

    internal static ResolvedDirectory Cache() => Cache(
        Environment.GetEnvironmentVariable,
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    // A blank variable counts as unset; a relative one would resolve against whatever the working
    // directory happens to be, so it is rejected with a warning for the caller to log.
    internal static ResolvedDirectory Cache(Func<string, string?> getEnvironmentVariable, string localAppData)
    {
        string fallback = Path.Combine(localAppData, AppFolderName);
        string? value = getEnvironmentVariable(CacheDirVariable)?.Trim();
        if (string.IsNullOrEmpty(value))
            return new ResolvedDirectory(fallback);

        if (!Path.IsPathFullyQualified(value))
            return new ResolvedDirectory(fallback, $"{CacheDirVariable} is not an absolute path ({value}); using {fallback}.");

        return new ResolvedDirectory(value);
    }
}
