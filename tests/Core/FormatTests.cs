using System.Globalization;
using DevPilot.Core;

namespace DevPilot.Tests.Core;

public class FormatTests
{
    public FormatTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0L, "—")]
    [InlineData(512L, "512 B")]
    [InlineData(1536L, "2 KB")]
    [InlineData(5L * 1024 * 1024, "5 MB")]
    [InlineData(1610612736L, "1.5 GB")]
    [InlineData(2199023255552L, "2.0 TB")]
    public void Size_UsesTheLargestFittingUnit(long? bytes, string expected)
    {
        Assert.Equal(expected, Format.Size(bytes));
    }
}
