namespace ControllerMagic;

// Facts about the HidHide and ViGEmBus installers, shared by the downloading app and the elevated
// copy that runs them - kept apart from DriverInstaller so the elevated side needs no HTTP clients.
internal static class DriverPackages
{
    public const string HidHideFileName = "HidHideSetup.exe";
    public const string VigemFileName = "ViGEmBusSetup.exe";

    // Both real installers are a few MB; this only stops a hostile or broken download filling the disk.
    public const long MaxInstallerBytes = 50L * 1024 * 1024;

    // Confirmed by inspecting the Authenticode signature on the real installers for both projects
    // (same certificate, thumbprint 1F431092EC96A80B41AB5317F53AC02EA6F9B89B) - this is the
    // Common Name AuthenticodeVerifier checks against, not the full certificate subject.
    public const string ExpectedSigner = "Nefarius Software Solutions e.U.";

    // Windows Installer's well-known "succeeded, but a reboot is needed to finish" exit code -
    // both installers are Advanced-Installer-built (MSI-compatible), so this applies to either.
    private const int ErrorSuccessRebootRequired = 3010;

    // Advanced Installer's EXE bootstrapper forwards unrecognized switches to the underlying
    // msiexec, so this is standard MSI silent-install syntax. /norestart is not optional: without
    // it, a driver install that needs a reboot to finish just reboots the machine immediately and
    // unprompted - confirmed the hard way on a real machine before this was added. Exit code 3010
    // (ErrorSuccessRebootRequired) still shows up when a reboot is genuinely needed; it's read
    // back and surfaced to the user instead of either rebooting or silently ignoring it.
    public static readonly IReadOnlyList<string> SilentInstallArgs = ["/exenoui", "/qn", "/norestart"];

    public static InstallerStepResult StepResultFromExitCode(int exitCode) => exitCode switch
    {
        0 => InstallerStepResult.Succeeded,
        ErrorSuccessRebootRequired => InstallerStepResult.RebootRequired,
        _ => InstallerStepResult.Failed,
    };
}
