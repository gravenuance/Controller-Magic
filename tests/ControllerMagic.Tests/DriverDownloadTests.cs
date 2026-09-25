using System.Net.Sockets;
using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public sealed class DriverDownloadTests : IDisposable
{
    private const int ErrorDiskFull = unchecked((int)0x80070070);

    private readonly string _directory = Directory.CreateTempSubdirectory("cm-download-").FullName;

    private string Destination => Path.Combine(_directory, "Setup.exe");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    // A stream that can't report its length, so HttpContent has no Content-Length to go on.
    private sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }

    private static StreamContent WithoutContentLength(int bytes)
    {
        var content = new StreamContent(new UnknownLengthStream(new byte[bytes]));
        Assert.Null(content.Headers.ContentLength);
        return content;
    }

    [Fact]
    public async Task SaveAsync_WritesTheFinalFileAndLeavesNoPartial()
    {
        using var content = new ByteArrayContent([1, 2, 3]);

        await DriverInstaller.SaveAsync(content, Destination, maxBytes: 10, TestContext.Current.CancellationToken);

        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Destination, TestContext.Current.CancellationToken));
        Assert.Equal([Destination], Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task SaveAsync_DeclaredLengthOverTheCap_ThrowsWithoutWritingAnything()
    {
        using var content = new ByteArrayContent(new byte[11]);
        content.Headers.ContentLength = 11;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            DriverInstaller.SaveAsync(content, Destination, maxBytes: 10, TestContext.Current.CancellationToken));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task SaveAsync_UndeclaredLengthThatExceedsTheCap_ThrowsAndCleansUp()
    {
        using var content = WithoutContentLength(11);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            DriverInstaller.SaveAsync(content, Destination, maxBytes: 10, TestContext.Current.CancellationToken));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task SaveAsync_ExactlyAtTheCap_Succeeds()
    {
        using var content = WithoutContentLength(10);

        await DriverInstaller.SaveAsync(content, Destination, maxBytes: 10, TestContext.Current.CancellationToken);

        Assert.Equal(10, new FileInfo(Destination).Length);
    }

    [Fact]
    public async Task SaveAsync_FailureKeepsAnEarlierCompleteFileIntact()
    {
        await File.WriteAllBytesAsync(Destination, [9, 9], TestContext.Current.CancellationToken);
        using var content = WithoutContentLength(11);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            DriverInstaller.SaveAsync(content, Destination, maxBytes: 10, TestContext.Current.CancellationToken));

        Assert.Equal([9, 9], await File.ReadAllBytesAsync(Destination, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ClassifyDownloadFailure_DiskErrorsAreNotReportedAsNetworkErrors()
    {
        Assert.Equal(InstallOutcome.DiskError, DriverInstaller.ClassifyDownloadFailure(new IOException("There is not enough space on the disk.", ErrorDiskFull)));
        Assert.Equal(InstallOutcome.DiskError, DriverInstaller.ClassifyDownloadFailure(new UnauthorizedAccessException()));
    }

    [Fact]
    public void ClassifyDownloadFailure_NetworkShapedFailuresAreNetworkErrors()
    {
        Assert.Equal(InstallOutcome.NetworkError, DriverInstaller.ClassifyDownloadFailure(new HttpRequestException("no route")));
        Assert.Equal(InstallOutcome.NetworkError, DriverInstaller.ClassifyDownloadFailure(new HttpIOException(HttpRequestError.ResponseEnded)));
        Assert.Equal(InstallOutcome.NetworkError, DriverInstaller.ClassifyDownloadFailure(new IOException("reset", new SocketException())));
        Assert.Equal(InstallOutcome.NetworkError, DriverInstaller.ClassifyDownloadFailure(new TaskCanceledException("timed out", new TimeoutException())));
        Assert.Equal(InstallOutcome.NetworkError, DriverInstaller.ClassifyDownloadFailure(new TimeoutException()));
    }

    [Fact]
    public void ClassifyDownloadFailure_OversizedOrUnexpectedFailuresAreInstallFailures()
    {
        Assert.Equal(InstallOutcome.InstallFailed, DriverInstaller.ClassifyDownloadFailure(new InvalidDataException("too big")));
        Assert.Equal(InstallOutcome.InstallFailed, DriverInstaller.ClassifyDownloadFailure(new InvalidOperationException()));
    }
}
