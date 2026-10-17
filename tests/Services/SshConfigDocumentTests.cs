using DevPilot.Models;
using DevPilot.Services.Ssh;

namespace DevPilot.Tests.Services;

public class SshConfigDocumentTests
{
    // Multi-line string literals take the line endings of this file (CRLF, see .gitattributes);
    // the sample config is pinned to LF so the expected texts below are exact.
    private static readonly string Config = """
        # Global settings
        Include ~/.ssh/work/*.conf
        AddKeysToAgent yes

        # Production web server
        Host web prod-web
            HostName 203.0.113.10
            User deploy
            IdentityFile ~/.ssh/id_ed25519
            IdentityFile ~/.ssh/id_rsa

        Host github.com
          HostName=github.com
          User git
          IdentityFile "C:\Users\me\My Keys\github"

        Match host *.internal exec "test -f ~/.vpn"
            ProxyJump bastion

        Host *
            ServerAliveInterval 60
            User me
        """.ReplaceLineEndings("\n");

    private static SshConfigDocument Doc() => SshConfigDocument.Parse(Config);

    [Fact]
    public void Parse_AndToString_RoundTripsUnchanged()
    {
        Assert.Equal(Config + "\n", Doc().ToString());
    }

    [Fact]
    public void Parse_KeepsWindowsLineEndings()
    {
        string crlf = Config.Replace("\n", "\r\n") + "\r\n";

        Assert.Equal(crlf, SshConfigDocument.Parse(crlf).ToString());
    }

    [Fact]
    public void GetHosts_ReadsBlocksAndOptions()
    {
        var hosts = Doc().GetHosts();

        Assert.Equal(["web prod-web", "github.com", "host *.internal exec \"test -f ~/.vpn\"", "*"], hosts.Select(h => h.Patterns));

        var web = hosts[0];
        Assert.Equal("web", web.Alias);
        Assert.Equal("203.0.113.10", web.HostName);
        Assert.Equal("deploy", web.User);
        Assert.Equal("~/.ssh/id_ed25519", web.IdentityFile);
        // The second IdentityFile is kept and listed with the other options.
        Assert.Equal([new("IdentityFile", "~/.ssh/id_rsa")], web.OtherOptions);

        // "Keyword=value" and quoted values.
        Assert.Equal("github.com", hosts[1].HostName);
        Assert.Equal(@"C:\Users\me\My Keys\github", hosts[1].IdentityFile);

        Assert.True(hosts[2].IsMatchBlock);
        Assert.False(hosts[2].CanConnect);
        Assert.Null(hosts[3].Alias);   // "Host *" holds defaults, it is not a host to connect to
    }

    [Fact]
    public void Includes_AreReported()
    {
        Assert.Equal(["~/.ssh/work/*.conf"], Doc().Includes);
    }

    [Fact]
    public void AddHost_GoesBeforeCatchAllBlocks()
    {
        var doc = Doc();

        doc.AddHost(new SshHostEdit("db", "10.0.0.5", "postgres", "2222", null, "bastion", null));

        var hosts = doc.GetHosts();
        Assert.Equal(["web prod-web", "github.com", "db", "host *.internal exec \"test -f ~/.vpn\"", "*"], hosts.Select(h => h.Patterns));
        Assert.Equal("2222", hosts[2].Port);
        Assert.Contains("Host db\n    HostName 10.0.0.5\n    User postgres\n    Port 2222\n    ProxyJump bastion\n\nMatch host", doc.ToString());
    }

    [Fact]
    public void AddHost_ToEmptyFile()
    {
        var doc = SshConfigDocument.Parse("");

        doc.AddHost(new SshHostEdit("pi", "raspberrypi.local", "pi", null, @"C:\Keys\My Pi", null, "yes"));

        Assert.Equal("Host pi\n    HostName raspberrypi.local\n    User pi\n    IdentityFile \"C:\\Keys\\My Pi\"\n    ForwardAgent yes\n", doc.ToString());
    }

    [Fact]
    public void UpdateHost_ChangesOnlyEditedLines()
    {
        var doc = Doc();

        // New port, user removed, everything else the same.
        doc.UpdateHost(0, new SshHostEdit("web prod-web", "203.0.113.10", null, "2200", "~/.ssh/id_ed25519", null, null));

        string text = doc.ToString();
        Assert.Contains("# Production web server\nHost web prod-web\n    HostName 203.0.113.10\n    IdentityFile ~/.ssh/id_ed25519\n    IdentityFile ~/.ssh/id_rsa\n    Port 2200\n", text);
        Assert.DoesNotContain("User deploy", text);
        // Other blocks untouched, including their own indentation and syntax.
        Assert.Contains("  HostName=github.com\n", text);
    }

    [Fact]
    public void UpdateHost_KeepsTheBlocksIndentation()
    {
        var doc = Doc();

        doc.UpdateHost(1, new SshHostEdit("github.com", "github.com", "git", null, @"C:\Users\me\My Keys\github", null, "no"));

        Assert.Contains("  IdentityFile \"C:\\Users\\me\\My Keys\\github\"\n  ForwardAgent no\n", doc.ToString());
    }

    [Fact]
    public void RemoveHost_RemovesTheBlockAndItsComment()
    {
        var doc = Doc();

        doc.RemoveHost(0);

        string text = doc.ToString();
        Assert.DoesNotContain("Production web server", text);
        Assert.DoesNotContain("203.0.113.10", text);
        Assert.StartsWith("# Global settings\nInclude ~/.ssh/work/*.conf\nAddKeysToAgent yes\n\nHost github.com\n", text);
        Assert.Equal(3, doc.GetHosts().Count);
    }

    [Fact]
    public void RemoveHost_LastBlockLeavesNoTrailingBlankLines()
    {
        var doc = Doc();

        doc.RemoveHost(3);

        Assert.EndsWith("    ProxyJump bastion\n", doc.ToString());
    }
}
