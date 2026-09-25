using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

// The .NET runtime's own assemblies carry an embedded Microsoft Authenticode signature, so they
// make a signed sample that's always present without shipping a binary fixture.
public sealed class AuthenticodeVerifierTests : IDisposable
{
    private const string RuntimeSigner = ".NET";
    private static readonly string SignedFile = typeof(object).Assembly.Location;

    private readonly string _unsignedFile = Path.Combine(Path.GetTempPath(), $"cm-unsigned-{Guid.NewGuid():N}.exe");

    public void Dispose() => File.Delete(_unsignedFile);

    [Fact]
    public void IsSignedBy_SignedFileAndMatchingSigner_ReturnsTrue() =>
        Assert.True(AuthenticodeVerifier.IsSignedBy(SignedFile, RuntimeSigner));

    [Fact]
    public void IsSignedBy_SignedFileButDifferentSigner_ReturnsFalse() =>
        Assert.False(AuthenticodeVerifier.IsSignedBy(SignedFile, "Nefarius Software Solutions e.U."));

    [Fact]
    public void IsSignedBy_OpenHandle_VerifiesTheSameAsByPath()
    {
        using var file = new FileStream(SignedFile, FileMode.Open, FileAccess.Read, FileShare.Read);

        Assert.True(AuthenticodeVerifier.IsSignedBy(file, RuntimeSigner));
    }

    [Fact]
    public void IsSignedBy_UnsignedFile_ReturnsFalse()
    {
        File.WriteAllBytes(_unsignedFile, File.ReadAllBytes(SignedFile)[..4096]);

        Assert.False(AuthenticodeVerifier.IsSignedBy(_unsignedFile, RuntimeSigner));
    }

    [Fact]
    public void IsSignedBy_MissingFile_ReturnsFalse() =>
        Assert.False(AuthenticodeVerifier.IsSignedBy(_unsignedFile, RuntimeSigner));
}
