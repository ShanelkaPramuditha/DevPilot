using DevPilot.Services.Platform;

namespace DevPilot.Tests.Services;

public class ProcessRunnerTests
{
    private static async Task<(int ExitCode, List<string> Lines)> RunPowerShellAsync(string command)
    {
        var lines = new List<string>();
        int exitCode = await ProcessRunner.RunAsync("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", command], lines.Add, CancellationToken.None);
        return (exitCode, lines);
    }

    [Fact]
    public async Task RunAsync_ReportsWindowsStyleLines()
    {
        var (exitCode, lines) = await RunPowerShellAsync("[Console]::Out.Write(\"first`r`nsecond`r`n\"); exit 3");

        Assert.Equal(3, exitCode);
        Assert.Equal(["first", "second"], lines);
    }

    [Fact]
    public async Task RunAsync_KeepsOnlyTheLastStateOfAProgressLine()
    {
        // Progress bars redraw the same line with a carriage return.
        var (_, lines) = await RunPowerShellAsync("[Console]::Out.Write(\"10%`r55%`r100%`r`nDone`n\")");

        Assert.Equal(["100%", "Done"], lines);
    }

    [Fact]
    public async Task RunAsync_CancellingStopsTheProgram()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProcessRunner.RunAsync("powershell.exe", ["-NoProfile", "-Command", "Start-Sleep 30"], _ => { }, cancellation.Token));
    }
}
