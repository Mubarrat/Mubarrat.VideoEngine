using Mubarrat.OpenType.CommonTables;
using System.Buffers.Binary;
using System.Text;

namespace Mubarrat.OpenType;

public sealed class OpenTypeReader : IDisposable
{
    private readonly Stream stream;

    public OpenTypeReader(Stream stream)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable.", nameof(stream));
        this.stream = stream;
    }

    public long Length => stream.Length;

    public long Position
    {
        get => stream.Position;
        set => stream.Position = value;
    }

    public Stream Stream => stream;

    public void Seek(long offset, SeekOrigin origin = SeekOrigin.Begin)
        => stream.Seek(offset, origin);

    public bool CanRead(long offset, int count)
        => offset >= 0 && offset <= Length - count;

    private void Fill(Span<byte> buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = stream.Read(buffer[read..]);
            if (n == 0)
                throw new EndOfStreamException();
            read += n;
        }
    }

    public byte ReadUInt8()
    {
        int b = stream.ReadByte();
        if (b < 0) throw new EndOfStreamException();
        return (byte)b;
    }

    public sbyte ReadInt8() => unchecked((sbyte)ReadUInt8());

    public ushort ReadUInt16()
    {
        Span<byte> b = stackalloc byte[2];
        Fill(b);
        return BinaryPrimitives.ReadUInt16BigEndian(b);
    }

    public short ReadInt16()
    {
        Span<byte> b = stackalloc byte[2];
        Fill(b);
        return BinaryPrimitives.ReadInt16BigEndian(b);
    }

    public uint ReadUInt24()
    {
        Span<byte> b = stackalloc byte[3];
        Fill(b);
        return (uint)(b[0] << 16 | b[1] << 8 | b[2]); // Manually combine bytes (big-endian)
    }

    public uint ReadUInt32()
    {
        Span<byte> b = stackalloc byte[4];
        Fill(b);
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }

    public int ReadInt32()
    {
        Span<byte> b = stackalloc byte[4];
        Fill(b);
        return BinaryPrimitives.ReadInt32BigEndian(b);
    }

    public ulong ReadUInt64()
    {
        Span<byte> b = stackalloc byte[8];
        Fill(b);
        return BinaryPrimitives.ReadUInt64BigEndian(b);
    }

    public float ReadFixed() => ReadInt32() / 65536f;

    public short ReadFWord() => ReadInt16();

    public ushort ReadUFWord() => ReadUInt16();

    public float ReadF2Dot14() => ReadInt16() / 16384f;

    private const long SecondsBetween1904And1970 = 2082844800;

    public DateTime ReadLongDateTime() => new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(ReadUInt64());

    public string ReadTag() => Encoding.ASCII.GetString(ReadBytes(4));

    public byte ReadOffset8() => ReadUInt8();
    public ushort ReadOffset16() => ReadUInt16();
    public uint ReadOffset24() => ReadUInt24();
    public uint ReadOffset32() => ReadUInt32();

    public uint ReadVersion16Dot16() => ReadUInt32();

    public void ReadUInt16Array(Span<ushort> dest)
    {
        for (int i = 0; i < dest.Length; i++)
            dest[i] = ReadUInt16();
    }

    public ushort[] ReadUInt16Array(int count)
    {
        var arr = new ushort[count];
        ReadUInt16Array(arr);
        return arr;
    }

    public void ReadInt16Array(Span<short> dest)
    {
        for (int i = 0; i < dest.Length; i++)
            dest[i] = ReadInt16();
    }

    public short[] ReadInt16Array(int count)
    {
        var arr = new short[count];
        ReadInt16Array(arr);
        return arr;
    }

    public void ReadUInt24Array(Span<uint> dest)
    {
        for (int i = 0; i < dest.Length; i++)
            dest[i] = ReadUInt24();
    }

    public uint[] ReadUInt24Array(int count)
    {
        var arr = new uint[count];
        ReadUInt24Array(arr);
        return arr;
    }

    public void ReadUInt32Array(Span<uint> dest)
    {
        for (int i = 0; i < dest.Length; i++)
            dest[i] = ReadUInt32();
    }

    public uint[] ReadUInt32Array(int count)
    {
        var arr = new uint[count];
        ReadUInt32Array(arr);
        return arr;
    }

    public TableScope EnterScope(long baseOffset) => new(this, baseOffset, true);
    public TableScope ScopeOf(long baseOffset) => new(this, baseOffset, false);
    public TableScope EnterCurrentScope() => new(this, Position, true);
    public TableScope CurrentScope => new(this, Position, false);

    public T ParseCommonTable<T>(long baseOffset, object? param = null) where T : IOpenTypeCommonTable<T>
    {
        using (var scope = EnterScope(baseOffset))
            return T.Parse(scope, param);
    }

    public T? ParseCommonTableOrDefault<T>(long baseOffset, object? param = null) where T : IOpenTypeCommonTable<T>
    {
        if (baseOffset == 0)
            return default;
        using (var scope = EnterScope(baseOffset))
            return T.Parse(scope, param);
    }

    public T[] ParseCommonListTableContiguous<T>(long baseOffset, object? param = null) where T : IOpenTypeCommonTable<T>
    {
        using (var scope = EnterScope(baseOffset))
            return IOpenTypeCommonTable<T>.ParseListContiguous(scope, param);
    }

    public T[] ParseCommonListTableFromOffsets16<T>(long baseOffset, object? param = null) where T : IOpenTypeCommonTable<T>
    {
        using (var scope = EnterScope(baseOffset))
            return IOpenTypeCommonTable<T>.ParseListFromOffsets16(scope, param);
    }

    public readonly ref struct TableScope
    {
        private readonly long oldPosition;
        private readonly bool ownsPosition;

        public readonly long Base;
        public readonly OpenTypeReader Reader;

        internal TableScope(OpenTypeReader reader, long baseOffset, bool move)
        {
            Reader = reader;
            Base = baseOffset;

            if (move)
            {
                oldPosition = reader.Position;
                reader.Position = baseOffset;
                ownsPosition = true;
            }
            else
            {
                oldPosition = 0;
                ownsPosition = false;
            }
        }

        public static TableScope EnterScope(OpenTypeReader reader, long offset) => new(reader, offset, move: true);

        public static TableScope ScopeOf(OpenTypeReader reader, long offset) => new(reader, offset, move: false);

        public void Seek(long relativeOffset) => Reader.Position = Base + relativeOffset;

        public TableScope ScopeOf(long relativeOffset) => new(Reader, Base + relativeOffset, move: false);

        public TableScope EnterScope(long relativeOffset) => new(Reader, Base + relativeOffset, move: true);

        public T ParseCommonTable<T>(long relativeOffset, object? param = null) where T : IOpenTypeCommonTable<T>
        {
            if (relativeOffset == 0)
                return default;
            using (var scope = EnterScope(relativeOffset))
                return T.Parse(scope, param);
        }

        public T? ParseCommonTableOrDefault<T>(long relativeOffset, object? param = null) where T : IOpenTypeCommonTable<T>
        {
            if (relativeOffset == 0)
                return default;
            using (var scope = EnterScope(relativeOffset))
                return T.Parse(scope, param);
        }

        public T[] ParseCommonListTableContiguous<T>(long relativeOffset, object? param = null) where T : IOpenTypeCommonTable<T>
        {
            using (var scope = EnterScope(relativeOffset))
                return IOpenTypeCommonTable<T>.ParseListContiguous(scope, param);
        }

        public T[] ParseCommonListTableFromOffsets16<T>(long relativeOffset, object? param = null) where T : IOpenTypeCommonTable<T>
        {
            using (var scope = EnterScope(relativeOffset))
                return IOpenTypeCommonTable<T>.ParseListFromOffsets16(scope, param);
        }

        public void Dispose()
        {
            if (ownsPosition)
                Reader.Position = oldPosition;
        }
    }

    public void ReadBytes(Span<byte> buffer) => Fill(buffer);

    public byte[] ReadBytes(int count)
    {
        var arr = new byte[count];
        Fill(arr);
        return arr;
    }

    public void Dispose()
    {
        stream.Dispose();
        GC.SuppressFinalize(this);
    }
}
