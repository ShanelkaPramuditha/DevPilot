using DevPilot.Models;

namespace DevPilot.Tests.Models;

public class PortInfoTests
{
    [Theory]
    [InlineData("TCP", 3000, "0.0.0.0", true)]
    [InlineData("TCP", 8080, "::", true)]
    [InlineData("TCP", 5173, "127.0.0.1", true)]
    [InlineData("TCP", 53, "0.0.0.0", false)]      // DNS
    [InlineData("UDP", 3000, "0.0.0.0", false)]    // browsers need TCP
    [InlineData("TCP", 3000, "10.255.255.254", false)]
    public void CanOpenInBrowser_OnlyForTcpReachableAtLocalhost(string protocol, int port, string address, bool expected)
    {
        var info = new PortInfo(protocol, port, address, "node", 42);

        Assert.Equal(expected, info.CanOpenInBrowser);
    }

    [Fact]
    public void ProcessText_ShowsUnknownWhenOwnerIsNotReported()
    {
        Assert.Equal("unknown", new PortInfo("TCP", 53, "10.255.255.254", "", null).ProcessText);
        Assert.Equal("node (42)", new PortInfo("TCP", 3000, "0.0.0.0", "node", 42).ProcessText);
    }
}
