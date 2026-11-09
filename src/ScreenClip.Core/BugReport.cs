using System.Text.RegularExpressions;

namespace ScreenClip.Core;

/// <summary>
/// A bug report made by the app (asked by the author: a user's ScreenClip did not start, and nobody could tell why):
/// what it holds is cleaned of what names the user, and it opens as a new issue on GitHub, where the user writes what
/// happened and sends it, or not.
/// </summary>
public static class BugReport
{
    /// <summary>The longest address of a new issue: GitHub refuses much longer ones.</summary>
    public const int MaxUrlLength = 7000;

    /// <summary>
    /// <paramref name="text"/> without what names its user: the user's folder written %USERPROFILE%, the user's name
    /// and the computer's name replaced where they stand alone (not inside a longer word).
    /// </summary>
    public static string Anonymize(string text, string? userProfile, string? userName, string? machineName)
    {
        if (!string.IsNullOrEmpty(userProfile))
            text = text.Replace(userProfile.TrimEnd('\\', '/'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        foreach (var (name, mark) in new[] { (userName, "<user>"), (machineName, "<computer>") })
        {
            if (name is { Length: >= 2 })
                text = Regex.Replace(text, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}_])", mark, RegexOptions.IgnoreCase);
        }
        return text;
    }

    /// <summary>
    /// The page of a new issue of <paramref name="repository"/>, its title and body filled in: <paramref name="head"/>,
    /// <paramref name="details"/> and <paramref name="tail"/>. When it would be too long, the details lose lines from
    /// their end and <paramref name="cutNote"/> takes their place (<paramref name="cut"/>): the head and the tail, which
    /// close what the details open, stay.
    /// </summary>
    public static string NewIssueUrl(string repository, string title, string head, string details, string tail,
        string cutNote, out bool cut, int maxLength = MaxUrlLength)
    {
        string Url(string body) => $"https://github.com/{repository}/issues/new?labels=bug&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
        cut = false;
        string url = Url(head + details + tail);
        if (url.Length <= maxLength)
            return url;
        cut = true;
        var lines = details.Split('\n').ToList();
        while (lines.Count > 0)
        {
            lines.RemoveAt(lines.Count - 1);
            url = Url(head + string.Join('\n', lines) + "\n" + cutNote + "\n" + tail);
            if (url.Length <= maxLength)
                return url;
        }
        return Url(head + cutNote + "\n" + tail);
    }
}
