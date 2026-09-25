using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace ControllerMagic;

// The elevated half of a driver install (see docs/adr/0001-elevated-driver-install.md): copies the
// installers the unelevated app downloaded into an admin-only directory, verifies those copies and
// runs them. Runs headless in its own process and never starts the tray UI.
internal static class ElevatedDriverInstall
{
    // Not a protocol result, so the parent reports it as a generic failure.
    internal const int FailedExitCode = 2;

    // Generous for a silent driver install; a hung installer must not leave the parent waiting forever.
    private static readonly TimeSpan InstallerTimeout = TimeSpan.FromMinutes(15);

    public static int Run(IReadOnlyList<string> args)
    {
        try
        {
            if (!DriverInstallProtocol.TryParse(args, out var request))
            {
                AppLog.Default.Warning($"ElevatedDriverInstall: rejected arguments ({args.Count} given).");
                return FailedExitCode;
            }

            return Install(request);
        }
        catch (Exception ex)
        {
            // Top-level boundary of a headless process: log and report failure instead of crashing.
            AppLog.Default.Error("ElevatedDriverInstall: unexpected failure", ex);
            return FailedExitCode;
        }
    }

    private static int Install(DriverInstallRequest request)
    {
        bool wantHidHide = request.Drivers.HasFlag(DriverKinds.HidHide);
        bool wantVigem = request.Drivers.HasFlag(DriverKinds.Vigem);

        if (!IsElevated() || !PrepareStagingDirectory(out string stagingDirectory))
        {
            return DriverInstallProtocol.EncodeExitCode(
                wantHidHide ? InstallerStepResult.Failed : InstallerStepResult.NotRequested,
                wantVigem ? InstallerStepResult.Failed : InstallerStepResult.NotRequested);
        }

        var hidHide = wantHidHide
            ? InstallOne(request.SourceDirectory, stagingDirectory, DriverPackages.HidHideFileName)
            : InstallerStepResult.NotRequested;
        var vigem = wantVigem
            ? InstallOne(request.SourceDirectory, stagingDirectory, DriverPackages.VigemFileName)
            : InstallerStepResult.NotRequested;

        return DriverInstallProtocol.EncodeExitCode(hidHide, vigem);
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            return true;

        AppLog.Default.Warning("ElevatedDriverInstall: not running elevated.");
        return false;
    }

    private static bool PrepareStagingDirectory(out string stagingDirectory)
    {
        string appDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ControllerMagic");
        stagingDirectory = Path.Combine(appDirectory, "drivers");

        // Each level is checked, since whoever controls a parent can swap the child out from under us.
        return AdminOnlyDirectory.TryEnsure(appDirectory) && AdminOnlyDirectory.TryEnsure(stagingDirectory);
    }

    private static InstallerStepResult InstallOne(string sourceDirectory, string stagingDirectory, string fileName)
    {
        string stagedPath = Path.Combine(stagingDirectory, fileName);
        try
        {
            // Held open, denying writes and deletes, from verification until the installer has run.
            using var staged = Stage(Path.Combine(sourceDirectory, fileName), stagedPath);
            if (!AuthenticodeVerifier.IsSignedBy(staged, DriverPackages.ExpectedSigner))
            {
                AppLog.Default.Warning($"ElevatedDriverInstall: {fileName} failed signature verification.");
                return InstallerStepResult.SignatureInvalid;
            }

            int exitCode = RunInstaller(stagedPath);
            AppLog.Default.Info($"ElevatedDriverInstall: {fileName} exited with code {exitCode}.");
            return DriverPackages.StepResultFromExitCode(exitCode);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            AppLog.Default.Warning($"ElevatedDriverInstall: installing {fileName} failed", ex);
            return InstallerStepResult.Failed;
        }
        finally
        {
            TryDelete(stagedPath);
        }
    }

    // A fresh file in the admin-only directory, so nothing a standard user left there is reused.
    private static FileStream Stage(string sourcePath, string stagedPath)
    {
        File.Delete(stagedPath);
        using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var destination = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            if (source.Length > DriverPackages.MaxInstallerBytes)
                throw new IOException($"{sourcePath} is larger than {DriverPackages.MaxInstallerBytes} bytes.");
            source.CopyTo(destination);
        }

        return new FileStream(stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static int RunInstaller(string installerPath)
    {
        var psi = new ProcessStartInfo(installerPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in DriverPackages.SilentInstallArgs)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {installerPath}.");

        // Blocking is deliberate: this headless process has no UI thread and nothing else to do.
        if (process.WaitForExit(InstallerTimeout))
            return process.ExitCode;

        AppLog.Default.Warning($"ElevatedDriverInstall: {installerPath} still running after {InstallerTimeout}; giving up on it.");
        return -1;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Default.Warning($"ElevatedDriverInstall: could not remove {path}", ex);
        }
    }
}
