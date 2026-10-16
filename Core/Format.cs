namespace DevPilot.Core;

public static class Format
{
    public static string Size(long? bytes)
    {
        if (bytes is not { } value || value <= 0)
        {
            return "—";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = value;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit >= 3 ? $"{size:0.0} {units[unit]}" : $"{size:0} {units[unit]}";
    }
}
