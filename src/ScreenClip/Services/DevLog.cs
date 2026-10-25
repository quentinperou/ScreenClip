namespace ScreenClip.Services;

/// <summary>
/// The log of developer mode (Settings › About, as in TextCase), to understand what went wrong: captures (mode, size,
/// screen), the elements found under the pointer, recordings (area, sound devices, length), exports and errors.
/// **Never what a capture shows.** In %LOCALAPPDATA%\ScreenClip\screenclip.log, next to the settings, cut back to its
/// last half beyond 200 KB. Developer mode off: nothing is written.
/// </summary>
internal static class DevLog
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenClip", "screenclip.log");

    private const long MaxBytes = 200 * 1024;
    private static readonly object Lock = new();

    /// <summary>Developer mode is on.</summary>
    public static bool Enabled { get; set; }

    public static void Write(string line)
    {
        if (!Enabled)
            return;
        lock (Lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                {
                    var lines = File.ReadAllLines(FilePath);
                    File.WriteAllLines(FilePath, lines[(lines.Length / 2)..]);
                }
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
