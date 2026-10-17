using System.IO;
using DevPilot.Services.Ssh;

namespace DevPilot.Tests.Services;

public sealed class SshKeyServiceTests : IDisposable
{
    // Throwaway keys made for these tests; the expected fingerprints and sizes are what
    // "ssh-keygen -lf" printed for them.
    private const string Ed25519 = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIODUQKIANA2fOAfCVCFevi2h1sxaJtq27+K/vmojpU0N test@devpilot";

    private const string Rsa = "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQChg/dBGBSJg9VftUOblwYtMnlzFSqW2QOdVNmO72HxxLG19QicZeCCa3j0fKF3tgs7xy5grVHvaJ3oHnSU93QSaTTslbOOWFQBmM6msmbbzQ82Yeg7BVJceKMautB5mecL/tIKzXMhcvlnT8UtELRo2kErQ5vmyMtTG+JON/JSHCg4PUIor1W6apQdg/hvMk1apl7nXAfcku+PGPLhW68sZrrn4FrTHTD3I3xhW/my+sL0EsQi6Mq0mGg3dtTwhrB2aabW4XOx2QM+fcsoQE3DphVSPXiDGSAXkgzaw1/jsrc/AtUZA/fcZmXMqLX8SKsgQY4y+1qwnPh3BOkp4kqN rsa test";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "devpilot-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void ParsePublicKey_MatchesSshKeygenForEd25519()
    {
        var key = SshKeyService.ParsePublicKey(Ed25519)!;

        Assert.Equal("ED25519", key.Type);
        Assert.Equal(256, key.Bits);
        Assert.Equal("SHA256:5CGOhVyLJtW99qJ3xOtzPaFSoGY/tBV2288p4Bz1y7g", key.Fingerprint);
        Assert.Equal("test@devpilot", key.Comment);
    }

    [Fact]
    public void ParsePublicKey_MatchesSshKeygenForRsa()
    {
        var key = SshKeyService.ParsePublicKey(Rsa)!;

        Assert.Equal("RSA", key.Type);
        Assert.Equal(2048, key.Bits);
        Assert.Equal("SHA256:AQS9ejnMCyxW2ufLg1gb6gt2jNE1ZNIHgrVNofdB0I4", key.Fingerprint);
        Assert.Equal("rsa test", key.Comment);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ssh-ed25519")]
    [InlineData("ssh-ed25519 not-base64!")]
    public void ParsePublicKey_RejectsInvalidText(string text)
    {
        Assert.Null(SshKeyService.ParsePublicKey(text));
    }

    [Fact]
    public void FindKeys_PairsFilesAndLooksInSubfolders()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "keys", "work"));
        File.WriteAllText(Path.Combine(_folder, "id_ed25519"), "-----BEGIN OPENSSH PRIVATE KEY-----\nxxx\n");
        File.WriteAllText(Path.Combine(_folder, "id_ed25519.pub"), Ed25519);
        File.WriteAllText(Path.Combine(_folder, "keys", "work", "deploy"), "-----BEGIN OPENSSH PRIVATE KEY-----\nxxx\n");   // .pub missing
        File.WriteAllText(Path.Combine(_folder, "keys", "server.pub"), Rsa);                                                // private key elsewhere
        File.WriteAllText(Path.Combine(_folder, "config"), "Host x\n");
        File.WriteAllText(Path.Combine(_folder, "known_hosts"), "github.com ssh-ed25519 AAAA\n");

        var keys = SshKeyService.FindKeys(_folder);

        Assert.Equal(["id_ed25519", Path.Combine("keys", "server"), Path.Combine("keys", "work", "deploy")], keys.Select(k => k.Name));

        var pair = keys[0];
        Assert.NotNull(pair.PrivatePath);
        Assert.NotNull(pair.PublicPath);
        Assert.Null(pair.Problem);

        Assert.Null(keys[1].PrivatePath);
        Assert.Contains("Public key only", keys[1].Problem);

        Assert.Null(keys[2].PublicPath);
        Assert.Contains("missing", keys[2].Problem);
    }

    [Fact]
    public void FindKeys_MissingFolderIsEmpty()
    {
        Assert.Empty(SshKeyService.FindKeys(Path.Combine(_folder, "nope")));
    }
}
