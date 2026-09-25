using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Nefarius.Drivers.HidHide;

namespace ControllerMagic;

internal enum InstallOutcome
{
    Success,
    RebootRequired,
    NetworkError,
    DiskError,
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

    // Generous for a few-MB installer on a slow link, while still ending a stalled transfer.
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    // Two long-lived clients rather than one: HidHideSetupProvider needs its own base address and headers.
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

        string? hidHidePath = drivers.HasFlag(DriverKinds.HidHide) ? Path.Combine(DriversDirectory, DriverPackages.HidHideFileName) : null;
        string? vigemPath = drivers.HasFlag(DriverKinds.Vigem) ? Path.Combine(DriversDirectory, DriverPackages.VigemFileName) : null;

        progress?.Report("Downloading...");
        try
        {
            Directory.CreateDirectory(DriversDirectory);
            DeleteObsoleteFiles();
            if (hidHidePath != null)
                await DownloadHidHideAsync(hidHidePath, ct).ConfigureAwait(false);
            if (vigemPath != null)
                await DownloadAsync(VigemBusInstallerUrl, vigemPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && ct.IsCancellationRequested))
        {
            // Caught broadly: a third-party HTTP client (HidHideSetupProvider) can fail in ways this
            // app can't fully enumerate up front, and none of them should crash it.
            AppLog.Default.Warning("DriverInstaller: download failed", ex);
            return new InstallResult(ClassifyDownloadFailure(ex), ex.Message);
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
        using var timeout = StartDownloadTimeout(ct);
        using var response = await Client.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await SaveAsync(response.Content, destinationPath, DriverPackages.MaxInstallerBytes, timeout.Token).ConfigureAwait(false);
    }

    // HidHide ships its own setup-provider specifically so callers don't have to hardcode a
    // version/URL that goes stale - always fetches whatever is currently the latest release.
    private static async Task DownloadHidHideAsync(string destinationPath, CancellationToken ct)
    {
        using var timeout = StartDownloadTimeout(ct);
        var provider = new HidHideSetupProvider(HidHideClient);
        using var response = await provider.DownloadLatestReleaseAsync(timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await SaveAsync(response.Content, destinationPath, DriverPackages.MaxInstallerBytes, timeout.Token).ConfigureAwait(false);
    }

    // HttpClient.Timeout stops at the response headers, so this is what bounds a stalled body.
    private static CancellationTokenSource StartDownloadTimeout(CancellationToken ct)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(DownloadTimeout);
        return timeout;
    }

    // Streams into "<name>.part" and renames only once complete and within maxBytes, so an
    // interrupted or oversized download never leaves a file that looks finished.
    internal static async Task SaveAsync(HttpContent content, string destinationPath, long maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException($"Download is {content.Headers.ContentLength} bytes; the limit is {maxBytes}.");

        string partPath = destinationPath + ".part";
        try
        {
            var body = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using (body.ConfigureAwait(false))
            {
                var file = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);
                await using (file.ConfigureAwait(false))
                    await CopyBoundedAsync(body, file, maxBytes, ct).ConfigureAwait(false);
            }

            File.Move(partPath, destinationPath, overwrite: true);
        }
        catch
        {
            TryDeletePartial(partPath);
            throw;
        }
    }

    private static async Task CopyBoundedAsync(Stream source, Stream destination, long maxBytes, CancellationToken ct)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new InvalidDataException($"Download exceeded the {maxBytes}-byte limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
    }

    private static void TryDeletePartial(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning($"DriverInstaller: failed to remove partial download {path}", ex);
        }
    }

    // Only transport failures mean "no network"; a full disk or locked file gets its own message.
    // HttpIOException is an IOException, so network checks have to come first.
    internal static InstallOutcome ClassifyDownloadFailure(Exception ex) => ex switch
    {
        HttpRequestException or HttpIOException or TimeoutException or OperationCanceledException => InstallOutcome.NetworkError,
        IOException { InnerException: SocketException } => InstallOutcome.NetworkError,
        IOException or UnauthorizedAccessException => InstallOutcome.DiskError,
        _ => InstallOutcome.InstallFailed,
    };

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
