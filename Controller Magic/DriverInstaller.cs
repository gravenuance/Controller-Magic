using System.ComponentModel;
using System.Diagnostics;
using Nefarius.Drivers.HidHide;

namespace ControllerMagic;

internal enum InstallOutcome
{
    Success,
    NetworkError,
    SignatureVerificationFailed,
    ElevationDeclined,
    InstallFailed,
}

internal readonly record struct InstallResult(InstallOutcome Outcome, string? Detail = null)
{
    public bool Succeeded => Outcome == InstallOutcome.Success;
}

// Quietly downloads and silently installs HidHide + ViGEmBus behind a single UAC prompt, the
// first time the "Suppress Guide button" toggle needs drivers that aren't present yet.
internal static class DriverInstaller
{
    // ViGEmBus is archived/unmaintained (no setup-provider library like HidHide's), so its
    // installer is pinned to the latest real release as of this writing rather than resolved
    // dynamically - confirmed live against github.com/nefarius/ViGEmBus/releases at the time this
    // was written. Bump by hand if a newer release ever appears.
    private const string VigemBusInstallerUrl =
        "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";

    // Confirmed by inspecting the Authenticode signature on the real installers for both projects
    // (same certificate, thumbprint 1F431092EC96A80B41AB5317F53AC02EA6F9B89B) - this is the
    // Common Name AuthenticodeVerifier checks against, not the full certificate subject.
    private const string ExpectedSigner = "Nefarius Software Solutions e.U.";

    // Advanced Installer's documented silent-install convention (both installers are built with
    // it, confirmed via each project's .aip setup file) - not hands-on verified against these
    // specific installers from this environment; confirm on a real machine before shipping (see
    // the manual test list in the implementation plan).
    private const string SilentInstallArgs = "/exenoui /qn";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(3) };

    private static string DriversDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerMagic", "drivers");

    public static async Task<InstallResult> InstallAsync(IProgress<string>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(DriversDirectory);
        string hidHidePath = Path.Combine(DriversDirectory, "HidHideSetup.exe");
        string vigemPath = Path.Combine(DriversDirectory, "ViGEmBusSetup.exe");

        progress?.Report("Downloading...");
        try
        {
            await DownloadHidHideAsync(hidHidePath, ct).ConfigureAwait(false);
            await DownloadAsync(VigemBusInstallerUrl, vigemPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            AppLog.Default.Warning("DriverInstaller: download failed", ex);
            return new InstallResult(InstallOutcome.NetworkError, ex.Message);
        }

        progress?.Report("Verifying...");
        if (!AuthenticodeVerifier.IsSignedBy(hidHidePath, ExpectedSigner) ||
            !AuthenticodeVerifier.IsSignedBy(vigemPath, ExpectedSigner))
        {
            AppLog.Default.Warning("DriverInstaller: a downloaded installer failed signature verification");
            return new InstallResult(InstallOutcome.SignatureVerificationFailed);
        }

        progress?.Report("Installing (approve the prompt)...");
        return await RunElevatedInstallAsync(hidHidePath, vigemPath, ct).ConfigureAwait(false);
    }

    private static async Task DownloadAsync(string url, string destinationPath, CancellationToken ct)
    {
        using var response = await Client.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var fileStream = File.Create(destinationPath);
        await response.Content.CopyToAsync(fileStream, ct).ConfigureAwait(false);
    }

    // HidHide ships its own setup-provider specifically so callers don't have to hardcode a
    // version/URL that goes stale - always fetches whatever is currently the latest release.
    private static async Task DownloadHidHideAsync(string destinationPath, CancellationToken ct)
    {
        var provider = new HidHideSetupProvider(Client);
        using var response = await provider.DownloadLatestReleaseAsync(ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var fileStream = File.Create(destinationPath);
        await response.Content.CopyToAsync(fileStream, ct).ConfigureAwait(false);
    }

    // A single elevated script runs both installers back-to-back, so the user sees exactly one
    // UAC prompt for both drivers - a temp .cmd file avoids the quoting hazards of building one
    // "cmd /c ... && ..." string for two paths that may contain spaces (a username with a space
    // in it puts one in %LocalAppData% itself). Mirrors StartupHelper's elevation pattern: a
    // Win32Exception with NativeErrorCode 1223 (ERROR_CANCELLED) means the user declined the UAC
    // prompt, distinct from every other failure.
    private static async Task<InstallResult> RunElevatedInstallAsync(string hidHidePath, string vigemPath, CancellationToken ct)
    {
        string scriptPath = Path.Combine(DriversDirectory, "install.cmd");
        string script = $"""
            @echo off
            "{hidHidePath}" {SilentInstallArgs}
            "{vigemPath}" {SilentInstallArgs}
            """;
        await File.WriteAllTextAsync(scriptPath, script, ct).ConfigureAwait(false);

        var psi = new ProcessStartInfo(scriptPath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
                return new InstallResult(InstallOutcome.InstallFailed, "Could not start the installer.");

            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return new InstallResult(InstallOutcome.Success);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            AppLog.Default.Warning("DriverInstaller: user declined the elevation prompt.");
            return new InstallResult(InstallOutcome.ElevationDeclined);
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning("DriverInstaller: elevated install failed", ex);
            return new InstallResult(InstallOutcome.InstallFailed, ex.Message);
        }
    }
}
