using System.IO.Compression;

namespace Viora.Application.Live;

public static class SvgaMetadata
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public static int Duration(byte[] data)
    {
        if (data.Length is < 8 or > MaxFileBytes) throw new ArgumentException("File SVGA tối đa 5 MB.");
        try
        {
            using var compressed = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = compressed.Read(buffer)) > 0)
            {
                if (output.Length + count > 24 * 1024 * 1024) throw new InvalidDataException();
                output.Write(buffer, 0, count);
            }
            var movie = output.ToArray();
            var parameters = Fields(movie).FirstOrDefault(f => f.Number == 2 && f.Wire == 2).Bytes;
            if (parameters is null) throw new InvalidDataException();
            var fields = Fields(parameters);
            var fps = fields.FirstOrDefault(f => f.Number == 3).Value;
            var frames = fields.FirstOrDefault(f => f.Number == 4).Value;
            var width = fields.FirstOrDefault(f => f.Number == 1).Value;
            var height = fields.FirstOrDefault(f => f.Number == 2).Value;
            var w = BitConverter.Int32BitsToSingle((int)width);
            var h = BitConverter.Int32BitsToSingle((int)height);
            if (!float.IsFinite(w) || !float.IsFinite(h) || w <= 0 || h <= 0 || w > 4096 || h > 4096 ||
                fps is < 1 or > 60 || frames is < 1 or > 1800 || frames * 1000L / fps > 30_000 ||
                !Fields(movie).Any(f => f.Number == 4 && f.Wire == 2)) throw new InvalidDataException();
            return (int)Math.Ceiling(frames * 1000d / fps);
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or OverflowException or ArgumentOutOfRangeException)
        { throw new ArgumentException("File SVGA 2 không hợp lệ hoặc hiệu ứng dài quá 30 giây."); }
    }

    private sealed record Field(int Number, int Wire, long Value, byte[]? Bytes = null);
    private static List<Field> Fields(byte[] data)
    {
        var result = new List<Field>(); var position = 0;
        long Varint() { long value = 0; for (var shift = 0; shift < 64; shift += 7) { if (position >= data.Length) throw new EndOfStreamException(); var b = data[position++]; value |= (long)(b & 127) << shift; if (b < 128) return value; } throw new InvalidDataException(); }
        while (position < data.Length)
        {
            var tag = Varint(); var number = checked((int)(tag >> 3)); var wire = (int)(tag & 7);
            if (number <= 0) throw new InvalidDataException();
            if (wire == 0) result.Add(new(number, wire, Varint()));
            else if (wire == 2) { var length = checked((int)Varint()); if (length < 0 || length > data.Length - position) throw new InvalidDataException(); result.Add(new(number, wire, 0, data.AsSpan(position, length).ToArray())); position += length; }
            else if (wire is 1 or 5) { var length = wire == 1 ? 8 : 4; if (length > data.Length - position) throw new InvalidDataException(); result.Add(new(number, wire, wire == 5 ? BitConverter.ToUInt32(data, position) : 0)); position += length; }
            else throw new InvalidDataException();
        }
        return result;
    }
}
