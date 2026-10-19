using System.Globalization;

namespace ScreenClip.Core;

/// <summary>
/// Names of the saved captures, from a template such as "Capture {date} à {time}":
/// {date} is 2026-10-19 and {time} 18.12.05 (dots: a colon cannot be in a file name), so that names sort by date.
/// </summary>
public static class FileNaming
{
    public const string DateToken = "{date}";
    public const string TimeToken = "{time}";

    /// <summary>The file name (without extension) for a capture taken at <paramref name="when"/>.</summary>
    public static string Expand(string template, DateTime when)
    {
        string name = template
            .Replace(DateToken, when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace(TimeToken, when.ToString("HH.mm.ss", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

        // Characters a file name cannot hold become spaces; trailing dots and spaces are dropped by Windows anyway.
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => Array.IndexOf(invalid, c) >= 0 ? ' ' : c).ToArray());
        name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.', ' ');
        return name.Length > 0 ? name : when.ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// "<paramref name="folder"/>\name.png", or "name (2).png", "name (3).png"… when the name is taken: a capture never
    /// overwrites a file.
    /// </summary>
    public static string UniquePath(string folder, string name, string extension, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        string path = Path.Combine(folder, name + extension);
        for (int n = 2; exists(path); n++)
            path = Path.Combine(folder, $"{name} ({n}){extension}");
        return path;
    }
}
