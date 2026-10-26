namespace ScreenClip.Services;

/// <summary>
/// Debug builds: a second ScreenClip that runs beside the one in use (<c>SCREENCLIP_INSTANCE=&lt;name&gt;</c>), with its
/// own single-instance names and temp folder, and its own settings file (<c>SCREENCLIP_SETTINGS=&lt;file&gt;</c>), so that
/// a test never touches the app being used nor its settings. Release builds: always the one ScreenClip.
/// </summary>
internal static class TestInstance
{
#if DEBUG
    public static readonly string Suffix =
        Environment.GetEnvironmentVariable("SCREENCLIP_INSTANCE") is { Length: > 0 } name ? "." + name : "";

    public static readonly string? SettingsFile =
        Environment.GetEnvironmentVariable("SCREENCLIP_SETTINGS") is { Length: > 0 } file ? file : null;
#else
    public const string Suffix = "";

    public const string? SettingsFile = null;
#endif
}
