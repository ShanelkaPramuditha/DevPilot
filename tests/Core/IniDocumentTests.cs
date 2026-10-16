using DevPilot.Core;

namespace DevPilot.Tests.Core;

public class IniDocumentTests
{
    private const string WslConf = "[boot]\nsystemd=true\n\n# keep me\n[user]\ndefault=shanelka\n";

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var doc = IniDocument.Parse(WslConf);

        Assert.Equal("true", doc.Get("BOOT", "Systemd"));
        Assert.Equal("shanelka", doc.Get("user", "DEFAULT"));
        Assert.Null(doc.Get("network", "hostname"));
    }

    [Fact]
    public void Set_ReplacesExistingValue()
    {
        var doc = IniDocument.Parse(WslConf);

        doc.Set("boot", "systemd", "false");

        Assert.Equal("false", doc.Get("boot", "systemd"));
        Assert.Contains("systemd=false", doc.ToString());
        Assert.DoesNotContain("systemd=true", doc.ToString());
    }

    [Fact]
    public void Set_AddsKeyAfterLastSettingOfSection_KeepingComments()
    {
        var doc = IniDocument.Parse(WslConf);

        doc.Set("boot", "command", "service docker start");

        string text = doc.ToString();
        Assert.True(text.IndexOf("command=", StringComparison.Ordinal) < text.IndexOf("# keep me", StringComparison.Ordinal),
            "The new key must stay in [boot], before the comment that introduces [user].");
        Assert.Contains("# keep me", text);
    }

    [Fact]
    public void Set_AddsMissingSection()
    {
        var doc = IniDocument.Parse("");

        doc.Set("wsl2", "memory", "4GB");

        Assert.Equal("[wsl2]\nmemory=4GB\n", doc.ToString());
    }

    [Fact]
    public void Set_WithEmptyValue_RemovesKeyAndEmptySection()
    {
        var doc = IniDocument.Parse(WslConf);

        doc.Set("user", "default", null);

        Assert.Null(doc.Get("user", "default"));
        Assert.DoesNotContain("[user]", doc.ToString());
        Assert.Contains("[boot]", doc.ToString());
    }

    [Fact]
    public void Get_StripsMatchingQuotes()
    {
        var doc = IniDocument.Parse("[wsl2]\nswapFile=\"C:\\\\temp\\\\swap.vhdx\"\n");

        Assert.Equal("C:\\\\temp\\\\swap.vhdx", doc.Get("wsl2", "swapFile"));
    }
}
