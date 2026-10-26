using ScreenClip.Core.Video;

namespace ScreenClip.Video;

/// <summary>What a sound of a recording is: the computer's, the microphone, both mixed, or one not known (another file).</summary>
internal enum SoundKind
{
    Mix,
    Computer,
    Microphone,
    Unknown,
}

/// <summary>
/// The sound tracks of a video file and what each one is. A recording with both sounds has three (the author's idea):
/// the mix, played by every player, then the computer and the microphone apart, as its alternates, for the editor;
/// ScreenClip names them when it records (<see cref="Mp4Tracks"/>), so that a file opened again has them apart too.
/// </summary>
internal static class SoundTracks
{
    private const string Prefix = "ScreenClip ";

    public static string NameOf(SoundKind kind) => Prefix + kind.ToString().ToLowerInvariant();

    /// <summary>
    /// Each sound of a recording has its own AAC rate, which Media Foundation tells (it does not list the tracks in the
    /// file's order, and its stream ids are not the tracks'): the mix 160 kbit/s, the computer 128, the microphone 96.
    /// </summary>
    public static uint BytesPerSecondOf(SoundKind kind) => kind switch
    {
        SoundKind.Computer => 16000,
        SoundKind.Microphone => 12000,
        _ => 20000,
    };

    /// <summary>Names the sound tracks of a recording just written (its sounds in their order); false if it could not.</summary>
    public static bool Mark(string path, IReadOnlyList<SoundKind> kinds)
    {
        try
        {
            return Mp4Tracks.MarkSounds(path, [.. kinds.Select(NameOf)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Services.DevLog.Write($"recording: sound tracks not named: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The sounds the editor shows, each with the rate its track is found by (null: the file's sound, the mix of a
    /// recording): the computer and the microphone apart when the file has them, else its sound alone.
    /// </summary>
    public static IReadOnlyList<(int? Track, SoundKind Kind)> Rows(string path)
    {
        IReadOnlyList<Mp4Tracks.Track> tracks;
        try
        {
            tracks = Mp4Tracks.Read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
        {
            tracks = [];
        }
        var sounds = tracks.Where(t => t.Handler == "soun").Select(t => (Track: (int?)BytesPerSecondOf(KindOf(t.Name)), Kind: KindOf(t.Name))).ToList();
        var apart = sounds.Where(s => s.Kind is SoundKind.Computer or SoundKind.Microphone).ToList();
        if (apart.Count > 0 && sounds.Count > 1 && Distinct(path, apart))
            return apart;
        // The file's first sound: the one Media Foundation plays (the mix of a recording).
        return [(null, sounds.Count == 1 ? sounds[0].Kind : SoundKind.Unknown)];
    }

    // Whether each sound apart is found by its rate (the first recordings with three sounds had them all at the same
    // rate: they open with their mix alone).
    private static bool Distinct(string path, IReadOnlyList<(int? Track, SoundKind Kind)> apart)
    {
        try
        {
            var rates = AudioReader.Rates(path);
            return apart.All(a => a.Track is { } rate && rates.Count(r => r == rate) == 1);
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or InvalidDataException)
        {
            return false;
        }
    }

    private static SoundKind KindOf(string name) =>
        Enum.GetValues<SoundKind>().FirstOrDefault(k => k != SoundKind.Unknown && name == NameOf(k), SoundKind.Unknown);
}
