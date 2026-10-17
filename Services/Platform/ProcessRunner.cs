using System.Diagnostics;
using System.IO;
using System.Text;

namespace DevPilot.Services.Platform;

/// <summary>
/// Runs a console program without a window and streams its output line by line,
/// for long operations whose progress is shown in the app (installs, updates).
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// Runs the program and returns its exit code. Every output line (standard output
    /// and errors) is passed to <paramref name="onLine"/>. Progress redraws that use a
    /// carriage return are reported once they are complete, not character by character.
    /// Cancelling kills the program and its children.
    /// </summary>
    public static async Task<int> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        Action<string> onLine,
        CancellationToken cancellationToken,
        string? standardInput = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (standardInput is not null)
        {
            startInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");

        var output = PumpAsync(process.StandardOutput, onLine);
        var errors = PumpAsync(process.StandardError, onLine);

        if (standardInput is not null)
        {
            // Linux shells need LF line endings.
            await process.StandardInput.WriteAsync(standardInput.ReplaceLineEndings("\n"));
            process.StandardInput.Close();
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        await Task.WhenAll(output, errors);
        return process.ExitCode;
    }

    /// <summary>Reads a stream and reports complete lines, without NUL characters (UTF-16 output from some tools).</summary>
    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        var line = new StringBuilder();
        var buffer = new char[4096];
        bool pendingReturn = false;
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                char c = buffer[i];
                if (c == '\0')
                {
                    continue;
                }

                if (pendingReturn)
                {
                    pendingReturn = false;
                    if (c != '\n')
                    {
                        // A carriage return without a line feed is a progress redraw on the
                        // same line: keep only the newest text.
                        line.Clear();
                    }
                }

                switch (c)
                {
                    case '\n':
                        Flush();
                        break;
                    case '\r':
                        pendingReturn = true;
                        break;
                    default:
                        line.Append(c);
                        break;
                }
            }
        }

        Flush();

        void Flush()
        {
            string text = line.ToString().TrimEnd();
            line.Clear();
            if (text.Length > 0)
            {
                onLine(text);
            }
        }
    }
}
