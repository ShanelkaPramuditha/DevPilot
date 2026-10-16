namespace DevPilot.Core;

/// <summary>
/// A minimal INI file editor for wsl.conf and .wslconfig. Changing a value
/// keeps everything else in the file (comments, ordering, unknown keys).
/// Section and key names are matched case-insensitively, as WSL does.
/// </summary>
public sealed class IniDocument
{
    private readonly List<string> _lines;

    private IniDocument(List<string> lines)
    {
        _lines = lines;
    }

    public static IniDocument Parse(string? text)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n').ToList();

        // Drop trailing blank lines; ToString adds one final newline.
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return new IniDocument(lines);
    }

    public string? Get(string section, string key)
    {
        int index = FindKey(section, key);
        return index < 0 ? null : SplitKeyValue(_lines[index])?.Value;
    }

    /// <summary>Sets a value, or removes the key when the value is null or empty.</summary>
    public void Set(string section, string key, string? value)
    {
        int index = FindKey(section, key);

        if (string.IsNullOrWhiteSpace(value))
        {
            if (index >= 0)
            {
                _lines.RemoveAt(index);
                RemoveSectionIfEmpty(section);
            }

            return;
        }

        string line = $"{key}={value.Trim()}";
        if (index >= 0)
        {
            _lines[index] = line;
            return;
        }

        int header = FindSection(section);
        if (header < 0)
        {
            if (_lines.Count > 0)
            {
                _lines.Add("");
            }

            _lines.Add($"[{section}]");
            _lines.Add(line);
            return;
        }

        // Insert after the last setting of the section, so comments that
        // introduce the next section stay with it.
        int insertAt = header + 1;
        for (int i = header + 1; i < _lines.Count && !IsSectionHeader(_lines[i], out _); i++)
        {
            if (SplitKeyValue(_lines[i]) is not null)
            {
                insertAt = i + 1;
            }
        }

        _lines.Insert(insertAt, line);
    }

    /// <summary>
    /// Removes a section header that no longer has any lines under it
    /// (written by this app when its last setting was cleared).
    /// </summary>
    private void RemoveSectionIfEmpty(string section)
    {
        int header = FindSection(section);
        if (header < 0)
        {
            return;
        }

        int end = header + 1;
        while (end < _lines.Count && !IsSectionHeader(_lines[end], out _))
        {
            if (_lines[end].Trim().Length > 0)
            {
                return;
            }

            end++;
        }

        _lines.RemoveRange(header, end - header);
        if (header > 0 && header <= _lines.Count && _lines[header - 1].Trim().Length == 0)
        {
            _lines.RemoveAt(header - 1);
        }
    }

    public override string ToString() => string.Join("\n", _lines) + "\n";

    private int FindSection(string section)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            if (IsSectionHeader(_lines[i], out string name) && name.Equals(section, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private int FindKey(string section, string key)
    {
        string current = "";
        for (int i = 0; i < _lines.Count; i++)
        {
            if (IsSectionHeader(_lines[i], out string name))
            {
                current = name;
                continue;
            }

            if (current.Equals(section, StringComparison.OrdinalIgnoreCase)
                && SplitKeyValue(_lines[i]) is { } pair
                && pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsSectionHeader(string line, out string name)
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            name = trimmed[1..^1].Trim();
            return true;
        }

        name = "";
        return false;
    }

    private static (string Key, string Value)? SplitKeyValue(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
        {
            return null;
        }

        int equals = trimmed.IndexOf('=');
        if (equals <= 0)
        {
            return null;
        }

        string value = trimmed[(equals + 1)..].Trim();

        // Strip matching quotes, which WSL also accepts.
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        return (trimmed[..equals].Trim(), value);
    }
}
