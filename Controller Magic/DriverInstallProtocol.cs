namespace ControllerMagic;

[Flags]
internal enum DriverKinds
{
    None = 0,
    HidHide = 1,
    Vigem = 2,
}

internal enum InstallerStepResult
{
    NotRequested = 0,
    Succeeded = 1,
    RebootRequired = 2,
    SignatureInvalid = 3,
    Failed = 4,
}

internal readonly record struct DriverInstallRequest(DriverKinds Drivers, string SourceDirectory);

// The contract between the unelevated app and the elevated copy of itself it launches to install
// drivers: the command line one builds and the other parses, and the exit code carrying each
// installer's result back.
internal static class DriverInstallProtocol
{
    public const string InstallFlag = "--install-drivers";
    public const string SourceFlag = "--source";

    // Any exit code outside this marker (a crash, a killed process) is never mistaken for a result.
    private const int ExitCodeMarker = 0x4D43_0000;
    private const int ExitCodeMarkerMask = unchecked((int)0xFFFF_FF00);
    private const int StepMask = 0xF;

    // Well above any real %LocalAppData% path, well below anything that could be abusive.
    private const int MaxSourceDirectoryLength = 1024;

    private static readonly (DriverKinds Drivers, string Token)[] DriverTokens =
    [
        (DriverKinds.HidHide, "hidhide"),
        (DriverKinds.Vigem, "vigem"),
        (DriverKinds.HidHide | DriverKinds.Vigem, "both"),
    ];

    public static bool IsInstallCommand(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[0], InstallFlag, StringComparison.Ordinal);

    public static IReadOnlyList<string> BuildArguments(DriverInstallRequest request)
    {
        string token = Array.Find(DriverTokens, t => t.Drivers == request.Drivers).Token
            ?? throw new ArgumentException($"No drivers selected: {request.Drivers}", nameof(request));
        if (!IsAcceptableSourceDirectory(request.SourceDirectory))
            throw new ArgumentException("Source directory must be a normalised absolute path.", nameof(request));

        return [InstallFlag, token, SourceFlag, request.SourceDirectory];
    }

    // Exactly the shape BuildArguments produces, nothing else - the elevated side trusts no extras.
    public static bool TryParse(IReadOnlyList<string> args, out DriverInstallRequest request)
    {
        request = default;
        if (args.Count != 4 ||
            !string.Equals(args[0], InstallFlag, StringComparison.Ordinal) ||
            !string.Equals(args[2], SourceFlag, StringComparison.Ordinal))
            return false;

        var match = Array.Find(DriverTokens, t => string.Equals(t.Token, args[1], StringComparison.Ordinal));
        if (match.Token is null || !IsAcceptableSourceDirectory(args[3]))
            return false;

        request = new DriverInstallRequest(match.Drivers, args[3]);
        return true;
    }

    // Fully qualified and already normalised, so no ".." or relative segment can redirect it.
    internal static bool IsAcceptableSourceDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxSourceDirectoryLength)
            return false;
        if (path.AsSpan().IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(path))
            return false;

        try
        {
            return string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static int EncodeExitCode(InstallerStepResult hidHide, InstallerStepResult vigem) =>
        ExitCodeMarker | (int)hidHide | ((int)vigem << 4);

    public static bool TryDecodeExitCode(int exitCode, out InstallerStepResult hidHide, out InstallerStepResult vigem)
    {
        hidHide = (InstallerStepResult)(exitCode & StepMask);
        vigem = (InstallerStepResult)((exitCode >> 4) & StepMask);
        return (exitCode & ExitCodeMarkerMask) == ExitCodeMarker &&
               Enum.IsDefined(hidHide) &&
               Enum.IsDefined(vigem);
    }
}
