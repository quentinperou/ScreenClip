using ScreenClip.Core.Video;

namespace ScreenClip.Video;

/// <summary>
/// The sound of a video as the editor makes it (step 3 of the video): each of its sounds read apart, at its volume,
/// with its fades, in its part, muted or not, then mixed, through the pieces kept one after the other (a few
/// milliseconds faded at each join, so that a cut makes no click). For playing and for the export, in 48 kHz 16-bit
/// stereo chunks with their time in what is kept.
/// </summary>
internal sealed class SoundComposer : IDisposable
{
    public const int Rate = AudioRecorder.Rate;
    private const int ChunkFrames = Rate / 20;
    private const int JoinFrames = Rate / 200; // 5 ms

    private readonly TrackStream[] _tracks;
    private readonly SoundOptions[] _options;
    private readonly IReadOnlyList<Piece> _pieces;
    private readonly TimeSpan _duration;
    private readonly float[] _mix = new float[ChunkFrames * 2];
    private readonly float[] _one = new float[ChunkFrames * 2];
    private int _piece;
    private long _inPiece;        // frames done in the current piece
    private long _done;           // frames done in all

    /// <param name="rows">Each sound: its track's id (null: the first sound) and its options.</param>
    /// <param name="pieces">What plays, in order (the video's own times).</param>
    public SoundComposer(string path, IReadOnlyList<(int? Track, SoundOptions Options)> rows, IReadOnlyList<Piece> pieces, TimeSpan duration)
    {
        _tracks = [.. rows.Select(r => new TrackStream(new AudioReader(path, r.Track)))];
        _options = [.. rows.Select(r => r.Options)];
        _pieces = pieces;
        _duration = duration;
    }

    /// <summary>The next chunk of the mix and its time from the start of what is kept; null at the end.</summary>
    public (TimeSpan Time, byte[] Pcm)? Next()
    {
        while (_piece < _pieces.Count && _inPiece >= FramesOf(_pieces[_piece].Length))
        {
            _piece++;
            _inPiece = 0;
        }
        if (_piece >= _pieces.Count)
            return null;
        var piece = _pieces[_piece];
        long pieceFrames = FramesOf(piece.Length);
        int frames = (int)Math.Min(ChunkFrames, pieceFrames - _inPiece);
        long first = FramesOf(piece.Start) + _inPiece;
        Array.Clear(_mix, 0, frames * 2);
        for (int t = 0; t < _tracks.Length; t++)
        {
            var options = _options[t];
            if (options.Muted || options.Volume <= 0)
                continue;
            _tracks[t].Read(first, frames, _one);
            for (int f = 0; f < frames; f++)
            {
                float gain = (float)options.GainAt(TimeSpan.FromTicks((first + f) * 10_000_000L / Rate), _duration);
                if (gain == 0)
                    continue;
                _mix[2 * f] += _one[2 * f] * gain;
                _mix[2 * f + 1] += _one[2 * f + 1] * gain;
            }
        }
        // The joins between pieces: out and in again over a few milliseconds.
        for (int f = 0; f < frames; f++)
        {
            long at = _inPiece + f;
            float ramp = 1;
            if (_piece > 0 && at < JoinFrames)
                ramp = Math.Min(ramp, at / (float)JoinFrames);
            if (_piece < _pieces.Count - 1 && pieceFrames - at <= JoinFrames)
                ramp = Math.Min(ramp, (pieceFrames - at - 1) / (float)JoinFrames);
            if (ramp < 1)
            {
                _mix[2 * f] *= ramp;
                _mix[2 * f + 1] *= ramp;
            }
        }
        var pcm = new byte[frames * 4];
        for (int i = 0; i < frames * 2; i++)
        {
            short value = (short)Math.Clamp(_mix[i] * 32767f, short.MinValue, short.MaxValue);
            pcm[2 * i] = (byte)value;
            pcm[2 * i + 1] = (byte)(value >> 8);
        }
        var time = TimeSpan.FromTicks(_done * 10_000_000L / Rate);
        _inPiece += frames;
        _done += frames;
        return (time, pcm);
    }

    private static long FramesOf(TimeSpan time) => (long)Math.Round(time.TotalSeconds * Rate);

    public void Dispose()
    {
        foreach (var track in _tracks)
            track.Dispose();
    }

    /// <summary>One sound track read frame by frame from any point: blocks as they come, silence where there is none.</summary>
    private sealed class TrackStream(AudioReader reader) : IDisposable
    {
        private short[] _block = [];
        private long _blockStart;     // the frame of the video the block starts at
        private int _blockFrames;
        private long _next = -1;      // the frame expected next (a jump elsewhere seeks)
        private bool _ended;

        /// <summary>Frames from <paramref name="first"/> (the video's own frame number) into <paramref name="into"/>, in floats.</summary>
        public void Read(long first, int frames, float[] into)
        {
            if (first != _next)
            {
                reader.Seek(TimeSpan.FromTicks(first * 10_000_000L / Rate));
                _block = [];
                _blockFrames = 0;
                _blockStart = 0;
                _ended = false;
            }
            for (int f = 0; f < frames; f++)
            {
                long frame = first + f;
                while (!_ended && frame >= _blockStart + _blockFrames)
                {
                    if (reader.Next() is not { } block)
                    {
                        _ended = true;
                        break;
                    }
                    _blockStart = (long)Math.Round(block.Time.TotalSeconds * Rate);
                    _blockFrames = block.Pcm.Length / 4;
                    if (_block.Length < _blockFrames * 2)
                        _block = new short[_blockFrames * 2];
                    Buffer.BlockCopy(block.Pcm, 0, _block, 0, _blockFrames * 4);
                }
                if (_ended || frame < _blockStart)
                {
                    into[2 * f] = into[2 * f + 1] = 0;
                    continue;
                }
                int at = (int)(frame - _blockStart) * 2;
                into[2 * f] = _block[at] / 32768f;
                into[2 * f + 1] = _block[at + 1] / 32768f;
            }
            _next = first + frames;
        }

        public void Dispose() => reader.Dispose();
    }
}
