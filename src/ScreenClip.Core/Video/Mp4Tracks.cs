using System.Buffers.Binary;
using System.Text;

namespace ScreenClip.Core.Video;

/// <summary>
/// The tracks of an MP4 as its <c>moov</c> box lists them, and the marking of a recording's sound tracks: the mix first
/// and played, the sounds apart as its alternates (disabled), each with its name. Players then play the mix alone
/// (QuickTime would play every enabled track at once), and ScreenClip finds the sounds apart again by their names.
/// </summary>
public static class Mp4Tracks
{
    /// <summary>
    /// A track: its id (what Media Foundation knows it by: it does not list the tracks in the file's order), its
    /// handler (<c>vide</c>, <c>soun</c>…), its name, whether it plays, its alternate group.
    /// </summary>
    public sealed record Track(int Id, string Handler, string Name, bool Enabled, int AlternateGroup);

    // The boxes opened on the way to a track's header and handler; any other is kept as it is.
    private static readonly HashSet<string> Containers = ["moov", "trak", "mdia"];

    private sealed class Box(string type)
    {
        public string Type { get; } = type;
        public byte[] Payload { get; set; } = [];
        public List<Box>? Children { get; set; }

        public Box? Child(string type) => Children?.FirstOrDefault(c => c.Type == type);
    }

    public static IReadOnlyList<Track> Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public static IReadOnlyList<Track> Read(Stream stream)
    {
        if (FindMoov(stream) is not { } found)
            return [];
        return [.. Parse(found.Bytes).Child("moov")!.Children!.Where(b => b.Type == "trak").Select(TrackOf)];
    }

    /// <summary>
    /// Names the sound tracks of the file in their order (<paramref name="names"/>, one per track) and, when there are
    /// several, makes them alternates with only the first one playing. False when the file is not as expected (its
    /// sound tracks are left as they were).
    /// </summary>
    public static bool MarkSounds(string path, IReadOnlyList<string> names)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (FindMoov(stream) is not { } found)
            return false;
        var root = Parse(found.Bytes);
        var sounds = root.Child("moov")!.Children!.Where(b => b.Type == "trak" && Handler(b) == "soun").ToList();
        if (sounds.Count != names.Count || sounds.Count == 0)
            return false;
        for (int i = 0; i < sounds.Count; i++)
        {
            if (sounds[i].Child("tkhd") is not { } header || HeaderGroupOffset(header.Payload) is not { } group)
                return false;
            if (sounds.Count > 1)
            {
                BinaryPrimitives.WriteInt16BigEndian(header.Payload.AsSpan(group), 1);
                header.Payload[3] = (byte)(i == 0 ? header.Payload[3] | 0x01 : header.Payload[3] & ~0x01);
            }
            var handler = sounds[i].Child("mdia")!.Child("hdlr")!;
            handler.Payload = [.. handler.Payload.AsSpan(0, Math.Min(24, handler.Payload.Length)), .. Encoding.UTF8.GetBytes(names[i]), 0];
        }
        byte[] moov = Serialize(root.Child("moov")!);
        // The moov box is written last by Media Foundation: it can grow there without moving the media data, which
        // its chunk offsets point to.
        bool last = found.Offset + found.Bytes.Length == stream.Length;
        if (!last && moov.Length != found.Bytes.Length)
            return false;
        if (last)
            stream.SetLength(found.Offset);
        stream.Position = found.Offset;
        stream.Write(moov);
        return true;
    }

    private static Track TrackOf(Box trak)
    {
        string name = "";
        if (trak.Child("mdia")?.Child("hdlr") is { Payload.Length: > 24 } handler)
            name = Encoding.UTF8.GetString(handler.Payload.AsSpan(24)).TrimEnd('\0');
        bool enabled = true;
        int group = 0, id = 0;
        if (trak.Child("tkhd") is { } header && HeaderGroupOffset(header.Payload) is { } offset)
        {
            enabled = (header.Payload[3] & 0x01) != 0;
            group = BinaryPrimitives.ReadInt16BigEndian(header.Payload.AsSpan(offset));
            // After the version and the creation and modification times (32 or 64 bits).
            id = (int)BinaryPrimitives.ReadUInt32BigEndian(header.Payload.AsSpan(header.Payload[0] == 1 ? 20 : 12));
        }
        return new Track(id, Handler(trak), name, enabled, group);
    }

    private static string Handler(Box trak) =>
        trak.Child("mdia")?.Child("hdlr") is { Payload.Length: >= 12 } handler ? Encoding.ASCII.GetString(handler.Payload, 8, 4) : "";

    // Where the alternate group is in a track header: after the times, the track id and the reserved fields.
    private static int? HeaderGroupOffset(byte[] payload)
    {
        if (payload.Length < 4)
            return null;
        int offset = payload[0] == 1 ? 4 + 32 + 8 + 2 : 4 + 20 + 8 + 2;
        return payload.Length >= offset + 2 ? offset : null;
    }

    // The top-level moov box: where it starts and its bytes (header included).
    private static (long Offset, byte[] Bytes)? FindMoov(Stream stream)
    {
        long position = 0, length = stream.Length;
        Span<byte> header = stackalloc byte[16];
        while (position + 8 <= length)
        {
            stream.Position = position;
            stream.ReadExactly(header[..8]);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string type = Encoding.ASCII.GetString(header.Slice(4, 4));
            if (size == 1)
            {
                stream.ReadExactly(header.Slice(8, 8));
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.Slice(8, 8));
            }
            else if (size == 0)
                size = length - position;
            if (size < 8 || position + size > length)
                return null;
            if (type == "moov")
            {
                var bytes = new byte[size];
                stream.Position = position;
                stream.ReadExactly(bytes);
                return (position, bytes);
            }
            position += size;
        }
        return null;
    }

    // A box and what it holds (the containers on the way to the tracks' headers and handlers).
    private static Box Parse(byte[] bytes)
    {
        var root = new Box("") { Children = [] };
        ParseChildren(bytes, 0, bytes.Length, root.Children);
        return root;
    }

    private static void ParseChildren(byte[] bytes, int start, int end, List<Box> into)
    {
        int position = start;
        while (position + 8 <= end)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position));
            string type = Encoding.ASCII.GetString(bytes, position + 4, 4);
            int headerSize = 8;
            if (size == 1)
            {
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(position + 8));
                headerSize = 16;
            }
            else if (size == 0)
                size = end - position;
            if (size < headerSize || position + size > end)
                throw new InvalidDataException($"A box of the MP4 is cut: {type}.");
            var box = new Box(type);
            int payloadStart = position + headerSize, payloadEnd = (int)(position + size);
            if (Containers.Contains(type))
            {
                box.Children = [];
                ParseChildren(bytes, payloadStart, payloadEnd, box.Children);
            }
            else
                box.Payload = bytes[payloadStart..payloadEnd];
            into.Add(box);
            position = payloadEnd;
        }
    }

    private static byte[] Serialize(Box box)
    {
        using var payload = new MemoryStream();
        if (box.Children is { } children)
        {
            foreach (var child in children)
                payload.Write(Serialize(child));
        }
        else
            payload.Write(box.Payload);
        var bytes = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
        Encoding.ASCII.GetBytes(box.Type, bytes.AsSpan(4, 4));
        payload.ToArray().CopyTo(bytes, 8);
        return bytes;
    }
}
