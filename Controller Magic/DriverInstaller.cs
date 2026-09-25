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
    // ViGEmBus is archived/unmaintained (no setup-provider library like HidHide's), so its
    // installer is pinned to the latest real release as of this writing rather than resolved
    // dynamically - confirmed live against github.com/nefarius/ViGEmBus/releases at the time this
    // was written. Bump by hand if a newer release ever appears.
    private const string VigemBusInstallerUrl =
        "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";

    // Leftovers of the old cmd-script install, which this replaced.
    private static readonly string[] ObsoleteFiles = ["install.cmd", "hidhide.exitcode", "vigem.exitcode"];

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

    // Only a download cache: the elevated install copies from here into an admin-only directory.
    private static string DriversDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerMagic", "drivers");

    // currentStatus lets a caller that already ran DriverDependency.Detect skip re-checking, and
    // lets this method download/verify/install only whichever driver is actually missing - a
    // driver already present (e.g. HidHide installed by a previous attempt that then needed a
    // reboot) is never re-downloaded or re-run.
    public static async Task<InstallResult> InstallAsync(DriverStatus currentStatus, IProgress<string>? progress, CancellationToken ct)
    {
        var drivers = DriverKinds.None;
        if (!currentStatus.HidHideInstalled)
            drivers |= DriverKinds.HidHide;
        if (!currentStatus.VigemInstalled)
            drivers |= DriverKinds.Vigem;

        if (drivers == DriverKinds.None)
            return new InstallResult(InstallOutcome.Success);

        Directory.CreateDirectory(DriversDirectory);
        DeleteObsoleteFiles();
        string? hidHidePath = drivers.HasFlag(DriverKinds.HidHide) ? Path.Combine(DriversDirectory, DriverPackages.HidHideFileName) : null;
        string? vigemPath = drivers.HasFlag(DriverKinds.Vigem) ? Path.Combine(DriversDirectory, DriverPackages.VigemFileName) : null;

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

        // Only spares the user a pointless UAC prompt; the elevated copy re-verifies what it runs.
        progress?.Report("Verifying...");
        if ((hidHidePath != null && !AuthenticodeVerifier.IsSignedBy(hidHidePath, DriverPackages.ExpectedSigner)) ||
            (vigemPath != null && !AuthenticodeVerifier.IsSignedBy(vigemPath, DriverPackages.ExpectedSigner)))
        {
            AppLog.Default.Warning("DriverInstaller: a downloaded installer failed signature verification");
            return new InstallResult(InstallOutcome.SignatureVerificationFailed);
        }

        progress?.Report("Installing (approve the prompt)...");
        return await RunElevatedInstallAsync(drivers, ct).ConfigureAwait(false);
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

    private static void DeleteObsoleteFiles()
    {
        foreach (string name in ObsoleteFiles)
        {
            string path = Path.Combine(DriversDirectory, name);
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Default.Warning($"DriverInstaller: failed to clean up {path}", ex);
            }
        }
    }

    // One UAC prompt covers every missing driver: the app relaunches itself elevated, and that copy
    // re-verifies and runs the installers from an admin-only directory (see ElevatedDriverInstall).
    private static async Task<InstallResult> RunElevatedInstallAsync(DriverKinds drivers, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Application.ExecutablePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (string arg in DriverInstallProtocol.BuildArguments(new DriverInstallRequest(drivers, DriversDirectory)))
            psi.ArgumentList.Add(arg);

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
                return new InstallResult(InstallOutcome.InstallFailed, "Could not start the installer.");

            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return ToInstallResult(drivers, proc.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            AppLog.Default.Warning("DriverInstaller: user declined the elevation prompt.");
            return new InstallResult(InstallOutcome.ElevationDeclined);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            AppLog.Default.Warning("DriverInstaller: elevated install failed", ex);
            return new InstallResult(InstallOutcome.InstallFailed, ex.Message);
        }
    }

    // A requested driver the helper reports as not requested means it misbehaved, so that's a
    // failure rather than an assumed success.
    internal static InstallResult ToInstallResult(DriverKinds requested, int exitCode)
    {
        if (!DriverInstallProtocol.TryDecodeExitCode(exitCode, out var hidHide, out var vigem))
        {
            AppLog.Default.Warning($"DriverInstaller: elevated installer exited with unexpected code {exitCode}.");
            return new InstallResult(InstallOutcome.InstallFailed, $"Installer exit code: {exitCode}");
        }

        var steps = new List<(string Name, InstallerStepResult Result)>();
        if (requested.HasFlag(DriverKinds.HidHide))
            steps.Add(("HidHide", hidHide));
        if (requested.HasFlag(DriverKinds.Vigem))
            steps.Add(("ViGEmBus", vigem));

        if (steps.Exists(s => s.Result == InstallerStepResult.SignatureInvalid))
            return new InstallResult(InstallOutcome.SignatureVerificationFailed);

        var failed = steps.FindAll(s => s.Result is InstallerStepResult.Failed or InstallerStepResult.NotRequested);
        if (failed.Count > 0)
            return new InstallResult(InstallOutcome.InstallFailed, $"{string.Join(" and ", failed.ConvertAll(s => s.Name))} did not install.");

        return steps.Exists(s => s.Result == InstallerStepResult.RebootRequired)
            ? new InstallResult(InstallOutcome.RebootRequired)
            : new InstallResult(InstallOutcome.Success);
    }
}
