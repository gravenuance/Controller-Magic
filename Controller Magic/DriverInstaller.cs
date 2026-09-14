using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Nefarius.Drivers.HidHide;

namespace ControllerMagic;

internal enum InstallOutcome
{
    Success,
    RebootRequired,
    NetworkError,
    SignatureVerificationFailed,
    ElevationDeclined,
    InstallFailed,
}

internal readonly record struct InstallResult(InstallOutcome Outcome, string? Detail = null);

// Quietly downloads and silently installs whichever of HidHide/ViGEmBus aren't already present,
// behind a single UAC prompt, the first time the "Use HidHide" toggle needs them.
internal static class DriverInstaller
{
    // Windows Installer's well-known "succeeded, but a reboot is needed to finish" exit code -
    // both installers are Advanced-Installer-built (MSI-compatible), so this applies to either.
    private const int ErrorSuccessRebootRequired = 3010;

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

    // Advanced Installer's EXE bootstrapper forwards unrecognized switches to the underlying
    // msiexec, so this is standard MSI silent-install syntax. /norestart is not optional: without
    // it, a driver install that needs a reboot to finish just reboots the machine immediately and
    // unprompted - confirmed the hard way on a real machine before this was added. Exit code 3010
    // (ErrorSuccessRebootRequired) still shows up when a reboot is genuinely needed; it's read
    // back and surfaced to the user instead of either rebooting or silently ignoring it.
    private const string SilentInstallArgs = "/exenoui /qn /norestart";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(3) };

    // HidHideSetupProvider doesn't work with a bare HttpClient - it expects one pre-configured
    // exactly the way Nefarius.Drivers.HidHide's own DI registration (AddHidHide, in
    // ServiceCollectionExtensions.cs) sets it up: a specific BaseAddress plus these two headers.
    // This app has no DI container to do that wiring for us, so it's replicated here by hand -
    // without it, GetLatestReleaseAsync throws (a relative request URI with no BaseAddress set).
    private static readonly HttpClient HidHideClient = new()
    {
        Timeout = TimeSpan.FromMinutes(3),
        BaseAddress = new Uri("https://vicius.api.nefarius.systems/"),
    };

    static DriverInstaller()
    {
        HidHideClient.DefaultRequestHeaders.UserAgent.ParseAdd(nameof(HidHideSetupProvider));
        HidHideClient.DefaultRequestHeaders.Add(
            "X-Vicius-OS-Architecture", RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
    }

    private static string DriversDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerMagic", "drivers");

    // currentStatus lets a caller that already ran DriverDependency.Detect skip re-checking, and
    // lets this method download/verify/install only whichever driver is actually missing - a
    // driver already present (e.g. HidHide installed by a previous attempt that then needed a
    // reboot) is never re-downloaded or re-run.
    public static async Task<InstallResult> InstallAsync(DriverStatus currentStatus, IProgress<string>? progress, CancellationToken ct)
    {
        bool needHidHide = !currentStatus.HidHideInstalled;
        bool needVigem = !currentStatus.VigemInstalled;

        if (!needHidHide && !needVigem)
            return new InstallResult(InstallOutcome.Success);

        Directory.CreateDirectory(DriversDirectory);
        string? hidHidePath = needHidHide ? Path.Combine(DriversDirectory, "HidHideSetup.exe") : null;
        string? vigemPath = needVigem ? Path.Combine(DriversDirectory, "ViGEmBusSetup.exe") : null;

        progress?.Report("Downloading...");
        try
        {
            if (hidHidePath != null)
                await DownloadHidHideAsync(hidHidePath, ct).ConfigureAwait(false);
            if (vigemPath != null)
                await DownloadAsync(VigemBusInstallerUrl, vigemPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Caught broadly rather than just HttpRequestException/TaskCanceledException/IOException:
            // a third-party HTTP client (HidHideSetupProvider) can fail in ways this app can't fully
            // enumerate up front, and none of them should crash it - only genuinely network-shaped
            // failures get the more actionable "No network" outcome, everything else falls back to a
            // generic one.
            AppLog.Default.Warning("DriverInstaller: download failed", ex);
            var outcome = ex is HttpRequestException or TaskCanceledException or IOException
                ? InstallOutcome.NetworkError
                : InstallOutcome.InstallFailed;
            return new InstallResult(outcome, ex.Message);
        }

        progress?.Report("Verifying...");
        if ((hidHidePath != null && !AuthenticodeVerifier.IsSignedBy(hidHidePath, ExpectedSigner)) ||
            (vigemPath != null && !AuthenticodeVerifier.IsSignedBy(vigemPath, ExpectedSigner)))
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
        var provider = new HidHideSetupProvider(HidHideClient);
        using var response = await provider.DownloadLatestReleaseAsync(ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var fileStream = File.Create(destinationPath);
        await response.Content.CopyToAsync(fileStream, ct).ConfigureAwait(false);
    }

    // A single elevated script runs whichever installer(s) are needed back-to-back, so the user
    // sees exactly one UAC prompt no matter how many drivers are missing - a temp .cmd file
    // avoids the quoting hazards of building one "cmd /c ... && ..." string for paths that may
    // contain spaces (a username with a space in it puts one in %LocalAppData% itself). Each
    // installer's own %errorlevel% is captured to a marker file immediately after it runs,
    // because cmd.exe's own exit code for the whole script is only ever the *last* command's -
    // without this, a failing first installer followed by a succeeding second one would look
    // like a clean success.
    private static async Task<InstallResult> RunElevatedInstallAsync(string? hidHidePath, string? vigemPath, CancellationToken ct)
    {
        string scriptPath = Path.Combine(DriversDirectory, "install.cmd");
        string? hidHideExitPath = hidHidePath != null ? Path.Combine(DriversDirectory, "hidhide.exitcode") : null;
        string? vigemExitPath = vigemPath != null ? Path.Combine(DriversDirectory, "vigem.exitcode") : null;

        var lines = new List<string> { "@echo off" };
        if (hidHidePath != null)
        {
            lines.Add($"\"{hidHidePath}\" {SilentInstallArgs}");
            lines.Add($"echo %errorlevel% > \"{hidHideExitPath}\"");
        }
        if (vigemPath != null)
        {
            lines.Add($"\"{vigemPath}\" {SilentInstallArgs}");
            lines.Add($"echo %errorlevel% > \"{vigemExitPath}\"");
        }

        await File.WriteAllTextAsync(scriptPath, string.Join(Environment.NewLine, lines), ct).ConfigureAwait(false);

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

            int? hidHideExit = hidHideExitPath != null ? await ReadExitCodeAsync(hidHideExitPath, ct).ConfigureAwait(false) : null;
            int? vigemExit = vigemExitPath != null ? await ReadExitCodeAsync(vigemExitPath, ct).ConfigureAwait(false) : null;

            return CombineOutcome(hidHidePath != null, hidHideExit, vigemPath != null, vigemExit);
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

    private static async Task<int?> ReadExitCodeAsync(string path, CancellationToken ct)
    {
        try
        {
            string text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return int.TryParse(text.Trim(), out int code) ? code : null;
        }
        catch (Exception ex)
        {
            AppLog.Default.Warning($"DriverInstaller: failed to read installer exit code from {path}", ex);
            return null;
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                AppLog.Default.Warning($"DriverInstaller: failed to clean up {path}", ex);
            }
        }
    }

    // ran=false means that installer wasn't part of this run at all (already installed) and
    // contributes nothing; exitCode=null means it ran but the marker file couldn't be read, which
    // is treated as a failure rather than silently assumed successful. Internal (not private) so
    // it's directly unit-testable, the same way ResolveLoaded/ComputeShouldBeActive are elsewhere
    // in this codebase - pure decision logic kept separate from the process/file I/O around it.
    internal static InstallResult CombineOutcome(bool hidHideRan, int? hidHideExit, bool vigemRan, int? vigemExit)
    {
        var exitCodes = new List<int>();

        if (hidHideRan)
        {
            if (hidHideExit is not { } code)
                return new InstallResult(InstallOutcome.InstallFailed, "Could not determine whether HidHide installed successfully.");
            exitCodes.Add(code);
        }

        if (vigemRan)
        {
            if (vigemExit is not { } code)
                return new InstallResult(InstallOutcome.InstallFailed, "Could not determine whether ViGEmBus installed successfully.");
            exitCodes.Add(code);
        }

        if (exitCodes.Exists(c => c != 0 && c != ErrorSuccessRebootRequired))
            return new InstallResult(InstallOutcome.InstallFailed, $"Installer exit code(s): {string.Join(", ", exitCodes)}");

        return exitCodes.Contains(ErrorSuccessRebootRequired)
            ? new InstallResult(InstallOutcome.RebootRequired)
            : new InstallResult(InstallOutcome.Success);
    }
}
