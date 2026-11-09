using System.Reflection;
using ScreenClip.Core;

namespace ScreenClip.Services;

/// <summary>
/// The last errors of ScreenClip, kept even with developer mode off (asked by the author: a user's ScreenClip did not
/// start, and nothing told why), so that a bug report can show them. Cleaned of the user's folder and names before
/// they are written; the last five, in %LOCALAPPDATA%\ScreenClip\errors.log. Never what a capture shows.
/// </summary>
internal static class ErrorLog
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenClip", $"errors{TestInstance.Suffix}.log");

    private const int Kept = 5;
    private const int MaxEntryLength = 6000;
    private const string Separator = "----";
    private static readonly object Lock = new();
    private static (string Key, DateTime Time) s_last;

    /// <summary>Keeps <paramref name="error"/>, met in <paramref name="where"/> (startup, interface, background…).</summary>
    public static void Record(Exception error, string where)
    {
        DevLog.Write($"error ({where}): {error}");
        // Called while things go wrong: it must never throw itself.
        try
        {
            // The same error again at once (a drawing that fails at each frame) is kept once.
            string key = $"{where} {error.GetType()} {error.Message} {error.StackTrace}";
            lock (Lock)
            {
                if (s_last.Key == key && DateTime.UtcNow - s_last.Time < TimeSpan.FromSeconds(10))
                    return;
                s_last = (key, DateTime.UtcNow);
            }

            string entry = Clean($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  ScreenClip {Version}  {where}\n{error}");
            if (entry.Length > MaxEntryLength)
                entry = entry[..MaxEntryLength] + "\n…";
            lock (Lock)
            {
                var entries = Read().ToList();
                entries.Add(entry);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, string.Join($"\n{Separator}\n", entries.TakeLast(Kept)));
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>The errors kept, the oldest first.</summary>
    public static IReadOnlyList<string> Read()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            return [.. File.ReadAllText(FilePath).Replace("\r\n", "\n").Split($"\n{Separator}\n")
                .Select(e => e.Trim()).Where(e => e.Length > 0)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary><paramref name="text"/> without the user's folder, user name and computer name.</summary>
    public static string Clean(string text) =>
        BugReport.Anonymize(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.UserName, Environment.MachineName);

    /// <summary>The version, read without the updater: an error may come from anywhere, even its start.</summary>
    public static string Version =>
        typeof(ErrorLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";
}
