using System.Text.RegularExpressions;
using DevPilot.Models;

namespace DevPilot.Services.Ssh;

/// <summary>
/// An OpenSSH client config file (~/.ssh/config) that can be edited without losing
/// anything: lines that are not changed (comments, blank lines, indentation, Match
/// blocks, Include lines, unknown options) are written back exactly as they were.
/// </summary>
public sealed partial class SshConfigDocument
{
    /// <summary>The options the host dialog edits, in the order they are written for new hosts.</summary>
    public static readonly IReadOnlyList<string> EditedKeywords =
        ["HostName", "User", "Port", "IdentityFile", "ProxyJump", "ForwardAgent"];

    private readonly List<string> _lines;
    private readonly string _newline;

    private SshConfigDocument(List<string> lines, string newline)
    {
        _lines = lines;
        _newline = newline;
    }

    public static SshConfigDocument Parse(string? text)
    {
        text ??= "";
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new SshConfigDocument(lines, newline);
    }

    /// <summary>Files pulled in with Include; their hosts are not part of this document.</summary>
    public IReadOnlyList<string> Includes => _lines
        .Select(ParseLine)
        .Where(l => l is { Keyword: var k } && k.Equals("Include", StringComparison.OrdinalIgnoreCase))
        .Select(l => l!.Value.Value)
        .ToList();

    /// <summary>The Host and Match blocks, in file order.</summary>
    public List<SshHost> GetHosts()
    {
        var hosts = new List<SshHost>();
        var blocks = FindBlocks();
        for (int b = 0; b < blocks.Count; b++)
        {
            var (start, end) = blocks[b];
            var header = ParseLine(_lines[start])!.Value;
            bool isMatch = header.Keyword.Equals("Match", StringComparison.OrdinalIgnoreCase);

            var options = new List<KeyValuePair<string, string>>();
            for (int i = start + 1; i < end; i++)
            {
                if (ParseLine(_lines[i]) is { } option)
                {
                    options.Add(new(option.Keyword, option.Value));
                }
            }

            // The first occurrence of an edited option is shown in its own column; repeats
            // (for example a second IdentityFile) are listed with the other options.
            string? Take(string keyword)
            {
                int index = options.FindIndex(o => o.Key.Equals(keyword, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    return null;
                }

                string value = options[index].Value;
                options.RemoveAt(index);
                return value;
            }

            hosts.Add(new SshHost(
                BlockIndex: b,
                Patterns: header.Value,
                IsMatchBlock: isMatch,
                HostName: Take("HostName"),
                User: Take("User"),
                Port: Take("Port"),
                IdentityFile: Take("IdentityFile"),
                ProxyJump: Take("ProxyJump"),
                ForwardAgent: Take("ForwardAgent"),
                OtherOptions: options));
        }

        return hosts;
    }

    /// <summary>
    /// Adds a host. It goes before the first "Host *" or Match block: SSH uses the first
    /// value it finds for each option, so hosts placed after catch-all defaults would get
    /// the defaults instead of their own settings.
    /// </summary>
    public void AddHost(SshHostEdit edit)
    {
        var blocks = FindBlocks();
        int insertAt = _lines.Count;
        for (int b = 0; b < blocks.Count; b++)
        {
            var header = ParseLine(_lines[blocks[b].Start])!.Value;
            bool catchAll = header.Keyword.Equals("Match", StringComparison.OrdinalIgnoreCase)
                || header.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("*");
            if (catchAll)
            {
                insertAt = LeadingCommentStart(blocks[b].Start);
                break;
            }
        }

        var block = new List<string> { $"Host {edit.Patterns.Trim()}" };
        foreach (var (keyword, value) in EditedValues(edit))
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                block.Add($"    {keyword} {Quote(value.Trim())}");
            }
        }

        // Keep one blank line between blocks.
        if (insertAt > 0 && _lines[insertAt - 1].Trim().Length > 0)
        {
            block.Insert(0, "");
        }

        if (insertAt < _lines.Count && _lines[insertAt].Trim().Length > 0)
        {
            block.Add("");
        }

        _lines.InsertRange(insertAt, block);
    }

    /// <summary>
    /// Changes a block's patterns and edited options. Other lines of the block stay;
    /// an empty value removes that option.
    /// </summary>
    public void UpdateHost(int blockIndex, SshHostEdit edit)
    {
        var (start, end) = FindBlocks()[blockIndex];
        var header = ParseLine(_lines[start])!.Value;
        _lines[start] = $"{header.Indent}{header.Keyword} {edit.Patterns.Trim()}";

        foreach (var (keyword, value) in EditedValues(edit))
        {
            (start, end) = FindBlocks()[blockIndex];
            int existing = -1;
            for (int i = start + 1; i < end; i++)
            {
                if (ParseLine(_lines[i]) is { } line && line.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    existing = i;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                if (existing >= 0)
                {
                    _lines.RemoveAt(existing);
                }

                continue;
            }

            if (existing >= 0)
            {
                var line = ParseLine(_lines[existing])!.Value;
                _lines[existing] = $"{line.Indent}{line.Keyword} {Quote(value.Trim())}";
            }
            else
            {
                // After the block's last option, indented like its other options.
                int last = start;
                string indent = "    ";
                for (int i = start + 1; i < end; i++)
                {
                    if (ParseLine(_lines[i]) is { } option)
                    {
                        last = i;
                        indent = option.Indent;
                    }
                }

                _lines.Insert(last + 1, $"{indent}{keyword} {Quote(value.Trim())}");
            }
        }
    }

    /// <summary>Removes a block, with the comment lines directly above it.</summary>
    public void RemoveHost(int blockIndex)
    {
        var (start, end) = FindBlocks()[blockIndex];
        int from = LeadingCommentStart(start);

        // The block's own trailing blank lines go too, so no double gap is left.
        while (end > start + 1 && _lines[end - 1].Trim().Length == 0)
        {
            end--;
        }

        _lines.RemoveRange(from, end - from);

        // Collapse the blank lines left around the removed block.
        while (from < _lines.Count && from > 0 && _lines[from].Trim().Length == 0 && _lines[from - 1].Trim().Length == 0)
        {
            _lines.RemoveAt(from);
        }

        // At the start or end of the file, no blank lines are needed at all.
        while (from == 0 && _lines.Count > 0 && _lines[0].Trim().Length == 0)
        {
            _lines.RemoveAt(0);
        }

        while (from >= _lines.Count && _lines.Count > 0 && _lines[^1].Trim().Length == 0)
        {
            _lines.RemoveAt(_lines.Count - 1);
            from--;
        }
    }

    public override string ToString() => _lines.Count == 0 ? "" : string.Join(_newline, _lines) + _newline;

    private static IEnumerable<(string Keyword, string? Value)> EditedValues(SshHostEdit edit) =>
    [
        ("HostName", edit.HostName),
        ("User", edit.User),
        ("Port", edit.Port),
        ("IdentityFile", edit.IdentityFile),
        ("ProxyJump", edit.ProxyJump),
        ("ForwardAgent", edit.ForwardAgent),
    ];

    /// <summary>Each block runs from its Host/Match line to the next one (or the end of the file).</summary>
    private List<(int Start, int End)> FindBlocks()
    {
        var starts = new List<int>();
        for (int i = 0; i < _lines.Count; i++)
        {
            if (ParseLine(_lines[i]) is { } line
                && (line.Keyword.Equals("Host", StringComparison.OrdinalIgnoreCase)
                    || line.Keyword.Equals("Match", StringComparison.OrdinalIgnoreCase)))
            {
                starts.Add(i);
            }
        }

        var blocks = new List<(int, int)>();
        for (int b = 0; b < starts.Count; b++)
        {
            int end = b + 1 < starts.Count ? LeadingCommentStart(starts[b + 1]) : _lines.Count;
            blocks.Add((starts[b], end));
        }

        return blocks;
    }

    /// <summary>The first of the comment lines directly above a line (no blank line in between).</summary>
    private int LeadingCommentStart(int line)
    {
        int start = line;
        while (start > 0 && _lines[start - 1].TrimStart().StartsWith('#'))
        {
            start--;
        }

        return start;
    }

    /// <summary>One option line: its indentation, keyword as written, and value without quotes.</summary>
    private readonly record struct ConfigLine(string Indent, string Keyword, string Value);

    private static ConfigLine? ParseLine(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return null;
        }

        var match = OptionLine().Match(text);
        if (!match.Success)
        {
            return null;
        }

        string value = match.Groups[3].Value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        return new ConfigLine(match.Groups[1].Value, match.Groups[2].Value, value);
    }

    /// <summary>Values with spaces (Windows paths under "Program Files", for example) need quotes.</summary>
    private static string Quote(string value) =>
        value.Any(char.IsWhiteSpace) && !(value.StartsWith('"') && value.EndsWith('"')) ? $"\"{value}\"" : value;

    // "Keyword value" or "Keyword=value"; keywords are case-insensitive.
    [GeneratedRegex(@"^(\s*)([A-Za-z][A-Za-z0-9]*)(?:\s*=\s*|\s+)(.*)$")]
    private static partial Regex OptionLine();
}
