using System.Buffers.Binary;
using System.Text;
using ScreenClip.Core.Video;

namespace ScreenClip.Tests;

public class VideoEditTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(10);
    private static readonly TrimRange All = TrimRange.All(Duration);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Splits_MakePieces_InOrder()
    {
        var edit = new VideoEdit().SplitAt(S(6), Duration).SplitAt(S(3), Duration);
        Assert.Equal([new Piece(S(0), S(3)), new Piece(S(3), S(6)), new Piece(S(6), S(10))], edit.Pieces(Duration));
        Assert.Equal(new Piece(S(3), S(6)), edit.PieceAt(S(4), Duration));
        Assert.Equal(new Piece(S(3), S(6)), edit.PieceAt(S(3), Duration));
    }

    [Fact]
    public void ASplit_TooNearAnotherOrAnEnd_IsRefused()
    {
        var edit = new VideoEdit().SplitAt(S(3), Duration);
        Assert.Same(edit, edit.SplitAt(S(3.1), Duration));
        Assert.Same(edit, edit.SplitAt(S(0.1), Duration));
        Assert.Same(edit, edit.SplitAt(S(9.9), Duration));
    }

    [Fact]
    public void APieceRemoved_IsSkipped_AndTheTimesFollow()
    {
        var edit = new VideoEdit().SplitAt(S(3), Duration).SplitAt(S(6), Duration).RemoveAt(S(4), All, Duration);
        Assert.True(edit.IsRemoved(S(5)));
        Assert.Equal([new Piece(S(0), S(3)), new Piece(S(6), S(10))], edit.Kept(All));
        Assert.Equal(S(7), edit.KeptLength(All));
        Assert.Equal(S(4), edit.KeptTimeOf(S(7), All));
        Assert.Equal(S(3), edit.KeptTimeOf(S(4.5), All)); // removed: the start of the next piece kept
        Assert.Equal(S(7), edit.VideoTimeOf(S(4), All));
        Assert.Equal(S(1), edit.VideoTimeOf(S(1), All));
    }

    [Fact]
    public void ThePieceRemoved_ComesBack()
    {
        var edit = new VideoEdit().SplitAt(S(5), Duration).RemoveAt(S(2), All, Duration);
        Assert.Equal(S(5), edit.KeptLength(All));
        edit = edit.RestoreAt(S(2));
        Assert.False(edit.IsRemoved(S(2)));
        Assert.Equal(S(10), edit.KeptLength(All));
    }

    [Fact]
    public void TheLastPieceKept_CannotBeRemoved()
    {
        var edit = new VideoEdit().SplitAt(S(5), Duration).RemoveAt(S(2), All, Duration);
        Assert.Same(edit, edit.RemoveAt(S(7), All, Duration));
        // Nothing split: the whole video is one piece.
        var whole = new VideoEdit();
        Assert.Same(whole, whole.RemoveAt(S(2), All, Duration));
    }

    [Fact]
    public void TheHandles_AndTheRemovedPieces_Combine()
    {
        var trim = All.WithStart(S(1)).WithEnd(S(9), Duration);
        var edit = new VideoEdit().SplitAt(S(4), Duration).SplitAt(S(5), Duration).RemoveAt(S(4.5), trim, Duration);
        Assert.Equal([new Piece(S(1), S(4)), new Piece(S(5), S(9))], edit.Kept(trim));
        Assert.Equal([new Piece(S(2), S(4)), new Piece(S(5), S(9))], edit.KeptFrom(S(2), trim));
        Assert.Equal([new Piece(S(5), S(9))], edit.KeptFrom(S(4.5), trim));
    }

    [Fact]
    public void APiece_IsFramed_Alone_AndBackAsItWasAtOne()
    {
        var edit = new VideoEdit().SplitAt(S(3), Duration).SplitAt(S(6), Duration);
        var middle = edit.PieceAt(S(4), Duration);
        edit = edit.WithFraming(middle, 2, 0.25, 0.75);
        Assert.Null(edit.FramingAt(S(1)));
        Assert.Equal(2, edit.FramingAt(S(4))!.Value.Zoom);
        Assert.Null(edit.FramingAt(S(7)));
        edit = edit.WithFraming(middle, 1, 0.5, 0.5);
        Assert.Empty(edit.Framings);
    }

    [Fact]
    public void FramingAPart_OfAPieceFramed_CutsTheOtherAroundIt()
    {
        var edit = new VideoEdit().WithFraming(new Piece(S(0), S(10)), 2, 0.5, 0.5).WithFraming(new Piece(S(4), S(6)), 3, 0.2, 0.2);
        Assert.Equal(2, edit.FramingAt(S(2))!.Value.Zoom);
        Assert.Equal(3, edit.FramingAt(S(5))!.Value.Zoom);
        Assert.Equal(2, edit.FramingAt(S(8))!.Value.Zoom);
    }

    [Fact]
    public void TheRegionShown_StaysInThePicture()
    {
        var framing = new Framing(new Piece(S(0), S(1)), 2, 1, 0);
        var region = framing.Region(800, 600);
        Assert.Equal(400, region.Width, 3);
        Assert.Equal(300, region.Height, 3);
        Assert.Equal(400, region.Left, 3);
        Assert.Equal(0, region.Top, 3);
        Assert.Equal(new Framing(new Piece(S(0), S(1)), 9, -1, 2).Normalized(), new Framing(new Piece(S(0), S(1)), Framing.MaxZoom, 0, 1));
    }

    [Fact]
    public void TheSounds_KeepTheirOptions_ByRow()
    {
        var edit = new VideoEdit().WithSound(1, new SoundOptions { Volume = 50 });
        Assert.Equal(100, edit.Sound(0).Volume);
        Assert.Equal(50, edit.Sound(1).Volume);
        Assert.Equal(100, edit.Sound(5).Volume);
    }
}

public class SoundOptionsTests
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(10);

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void TheGain_FollowsTheVolume_AndIsSilentWhenMuted()
    {
        Assert.Equal(1, new SoundOptions().GainAt(S(5), Duration));
        Assert.Equal(1.5, new SoundOptions { Volume = 150 }.GainAt(S(5), Duration));
        Assert.Equal(0, new SoundOptions { Muted = true }.GainAt(S(5), Duration));
    }

    [Fact]
    public void Outside_ItsPart_ItIsSilent()
    {
        var options = new SoundOptions().WithStart(S(2), Duration).WithEnd(S(8), Duration);
        Assert.Equal(0, options.GainAt(S(1), Duration));
        Assert.Equal(1, options.GainAt(S(5), Duration));
        Assert.Equal(0, options.GainAt(S(8.5), Duration));
    }

    [Fact]
    public void TheFades_RiseAndFall_AtTheEndsOfItsPart()
    {
        var options = new SoundOptions { FadeIn = S(2), FadeOut = S(1) }.WithStart(S(2), Duration);
        Assert.Equal(0.5, options.GainAt(S(3), Duration), 3);
        Assert.Equal(1, options.GainAt(S(5), Duration), 3);
        Assert.Equal(0.5, options.GainAt(S(9.5), Duration), 3);
    }

    [Fact]
    public void ASoundSilenced_InAPiece_IsHeardAround()
    {
        var piece = new Piece(S(3), S(6));
        var options = new SoundOptions().WithSilenced(piece, true);
        Assert.True(options.IsSilencedAt(S(4)));
        Assert.Equal(0, options.GainAt(S(4), Duration));
        Assert.Equal(1, options.GainAt(S(2), Duration));
        Assert.Equal(1, options.GainAt(S(7), Duration));
        // A few milliseconds of ramp at its ends, no click.
        Assert.InRange(options.GainAt(S(6) + TimeSpan.FromMilliseconds(2), Duration), 0.1, 0.9);
        Assert.True(options.WithSilenced(piece, false).IsDefault);
    }

    [Fact]
    public void ThePart_StaysInTheVideo_AndAWholeOneIsTheDefault()
    {
        var options = new SoundOptions().WithStart(S(-3), Duration).WithEnd(S(30), Duration);
        Assert.True(options.IsDefault);
        var narrow = new SoundOptions().WithEnd(S(3), Duration).WithStart(S(5), Duration);
        Assert.True(narrow.Span(Duration).Length >= VideoEdit.MinPiece);
    }
}

public class Mp4TracksTests
{
    // A made-up MP4: ftyp, the media data, then moov with a video track and the sound tracks (as Media Foundation
    // writes it, moov last).
    private static byte[] Mp4(int sounds, bool moovLast = true)
    {
        static byte[] Box(string type, params byte[][] parts)
        {
            int length = 8 + parts.Sum(p => p.Length);
            var bytes = new byte[length];
            BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)length);
            Encoding.ASCII.GetBytes(type, bytes.AsSpan(4));
            int at = 8;
            foreach (var part in parts)
            {
                part.CopyTo(bytes, at);
                at += part.Length;
            }
            return bytes;
        }
        static byte[] Header(int id) => [0, 0, 0, 0x07, .. new byte[8], 0, 0, 0, (byte)id, .. new byte[8], .. new byte[8], 0, 0, 0, 0, .. new byte[2 + 2 + 36 + 8]];
        static byte[] Handler(string type) => [.. new byte[8], .. Encoding.ASCII.GetBytes(type), .. new byte[12], (byte)'x', 0];
        static byte[] Trak(string type, int id) => Box("trak", Box("tkhd", Header(id)), Box("mdia", Box("mdhd", new byte[24]), Box("hdlr", Handler(type)), Box("minf", new byte[16])));
        var traks = new List<byte[]> { Trak("vide", 1) };
        for (int i = 0; i < sounds; i++)
            traks.Add(Trak("soun", 2 + i));
        byte[] moov = Box("moov", [Box("mvhd", new byte[100]), .. traks]);
        byte[] ftyp = Box("ftyp", Encoding.ASCII.GetBytes("isom0000"));
        byte[] mdat = Box("mdat", [1, 2, 3, 4, 5, 6, 7, 8, 9]);
        return moovLast ? [.. ftyp, .. mdat, .. moov] : [.. ftyp, .. moov, .. mdat];
    }

    private static string Write(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"screenclip-test-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void TheSounds_AreNamed_TheMixPlays_TheOthersAreItsAlternates()
    {
        string path = Write(Mp4(3));
        try
        {
            Assert.True(Mp4Tracks.MarkSounds(path, ["ScreenClip mix", "ScreenClip computer", "ScreenClip microphone"]));
            var tracks = Mp4Tracks.Read(path);
            Assert.Equal(["vide", "soun", "soun", "soun"], tracks.Select(t => t.Handler));
            Assert.Equal([1, 2, 3, 4], tracks.Select(t => t.Id));
            var sounds = tracks.Where(t => t.Handler == "soun").ToList();
            Assert.Equal(["ScreenClip mix", "ScreenClip computer", "ScreenClip microphone"], sounds.Select(t => t.Name));
            Assert.Equal([true, false, false], sounds.Select(t => t.Enabled));
            Assert.All(sounds, t => Assert.Equal(1, t.AlternateGroup));
            Assert.True(tracks[0].Enabled);
            Assert.Equal(0, tracks[0].AlternateGroup);
            // The media data did not move.
            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, bytes.AsSpan(16 + 8, 9).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OneSound_IsNamed_AndStaysAsItWas()
    {
        string path = Write(Mp4(1));
        try
        {
            Assert.True(Mp4Tracks.MarkSounds(path, ["ScreenClip microphone"]));
            var sound = Mp4Tracks.Read(path).Single(t => t.Handler == "soun");
            Assert.Equal("ScreenClip microphone", sound.Name);
            Assert.True(sound.Enabled);
            Assert.Equal(0, sound.AlternateGroup);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AFileNotAsExpected_IsLeftAlone()
    {
        string path = Write(Mp4(2));
        try
        {
            byte[] before = File.ReadAllBytes(path);
            Assert.False(Mp4Tracks.MarkSounds(path, ["only one name"]));
            Assert.Equal(before, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
