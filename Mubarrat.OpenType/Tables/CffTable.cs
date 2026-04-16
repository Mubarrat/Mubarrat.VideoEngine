using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Mubarrat.OpenType.Tables;

public sealed class CffTable : IOpenTypeTable
{
    private const ushort TopDictCharsetOperator = 15;
    private const ushort TopDictEncodingOperator = 16;
    private const ushort TopDictCharStringsOperator = 17;
    private const ushort TopDictPrivateOperator = 18;
    private const ushort TopDictCharstringTypeOperator = 0x0C06;
    private const ushort PrivateDictSubrsOperator = 19;

    public string Tag => "CFF ";

    public byte Major { get; private set; }
    public byte Minor { get; private set; }
    public byte HeaderSize { get; private set; }
    public byte OffsetSize { get; private set; }

    public string FontName { get; private set; } = string.Empty;
    public byte[] TopDictData { get; private set; } = [];
    public CffDictEntry[] TopDictEntries { get; private set; } = [];
    public string[] Strings { get; private set; } = [];
    public byte[][] GlobalSubroutines { get; private set; } = [];
    public byte[][] CharStrings { get; private set; } = [];
    public byte[] PrivateDictData { get; private set; } = [];
    public CffDictEntry[] PrivateDictEntries { get; private set; } = [];
    public byte[][] LocalSubroutines { get; private set; } = [];

    public uint CharStringsOffset { get; private set; }
    public uint CharsetOffset { get; private set; }
    public uint EncodingOffset { get; private set; }
    public uint PrivateDictOffset { get; private set; }
    public uint PrivateDictSize { get; private set; }
    public byte CharstringType { get; private set; }
    public CffCharsetKind CharsetKind { get; private set; }
    public ushort[] CharsetSids { get; private set; } = [];

    public readonly record struct CffDictEntry(ushort Operator, double[] Operands);

    public enum CffCharsetKind
    {
        None,
        IsoAdobe,
        Expert,
        ExpertSubset,
        Format0,
        Format1,
        Format2
    }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        ArgumentNullException.ThrowIfNull(tables);

        var reader = scope.Reader;

        Major = reader.ReadUInt8();
        Minor = reader.ReadUInt8();
        HeaderSize = reader.ReadUInt8();
        OffsetSize = reader.ReadUInt8();

        if (HeaderSize < 4)
            throw new InvalidDataException("Invalid CFF header size.");

        scope.Seek(HeaderSize);

        var nameIndex = ParseIndex(reader);
        if (nameIndex.Length != 1)
            throw new InvalidDataException("CFF table must contain exactly one font in Name INDEX.");

        FontName = Encoding.ASCII.GetString(nameIndex[0]);

        var topDictIndex = ParseIndex(reader);
        if (topDictIndex.Length != 1)
            throw new InvalidDataException("CFF table must contain exactly one Top DICT.");

        TopDictData = topDictIndex[0];
        TopDictEntries = ParseDict(TopDictData);

        var stringIndex = ParseIndex(reader);
        Strings = new string[stringIndex.Length];
        for (int i = 0; i < stringIndex.Length; i++)
            Strings[i] = Encoding.ASCII.GetString(stringIndex[i]);

        GlobalSubroutines = ParseIndex(reader);

        CharstringType = (byte)Math.Round(GetSingleOperandOrDefault(TopDictEntries, TopDictCharstringTypeOperator, 2));
        if (CharstringType != 2)
            throw new InvalidDataException($"Unsupported CFF CharstringType '{CharstringType}'.");

        CharStringsOffset = GetRequiredUnsignedOperand(TopDictEntries, TopDictCharStringsOperator);
        CharsetOffset = GetOptionalUnsignedOperand(TopDictEntries, TopDictCharsetOperator, 0);
        EncodingOffset = GetOptionalUnsignedOperand(TopDictEntries, TopDictEncodingOperator, 0);

        (PrivateDictSize, PrivateDictOffset) = GetPrivateDictLocation(TopDictEntries);

        using (var charStringsScope = scope.EnterScope(CharStringsOffset))
            CharStrings = ParseIndex(charStringsScope.Reader);

        ParseCharset(scope, CharStrings.Length);

        if (PrivateDictSize > 0)
        {
            using var privateScope = scope.EnterScope(PrivateDictOffset);
            PrivateDictData = privateScope.Reader.ReadBytes((int)PrivateDictSize);
            PrivateDictEntries = ParseDict(PrivateDictData);

            uint localSubrsOffset = GetOptionalUnsignedOperand(PrivateDictEntries, PrivateDictSubrsOperator, 0);
            if (localSubrsOffset > 0)
            {
                using var subrsScope = scope.EnterScope(PrivateDictOffset + localSubrsOffset);
                LocalSubroutines = ParseIndex(subrsScope.Reader);
            }
        }

        tables.Add(this);
    }

    private static byte[][] ParseIndex(OpenTypeReader reader)
    {
        int count = reader.ReadUInt16();
        if (count == 0)
            return [];

        int offsetSize = reader.ReadUInt8();
        if ((uint)(offsetSize - 1) > 3)
            throw new InvalidDataException("Invalid CFF INDEX offset size.");

        int[] offsets = new int[count + 1];
        for (int i = 0; i < offsets.Length; i++)
            offsets[i] = ReadOffset(reader, offsetSize);

        if (offsets[0] != 1)
            throw new InvalidDataException("Invalid CFF INDEX offsets.");

        int dataLength = offsets[^1] - 1;
        if (dataLength < 0)
            throw new InvalidDataException("Invalid CFF INDEX data length.");

        byte[] rawData = reader.ReadBytes(dataLength);
        var objects = new byte[count][];

        for (int i = 0; i < count; i++)
        {
            int start = offsets[i] - 1;
            int end = offsets[i + 1] - 1;
            if (start < 0 || end < start || end > rawData.Length)
                throw new InvalidDataException("Invalid CFF INDEX object offsets.");

            objects[i] = rawData.AsSpan(start, end - start).ToArray();
        }

        return objects;
    }

    private static int ReadOffset(OpenTypeReader reader, int size)
    {
        int value = 0;
        for (int i = 0; i < size; i++)
            value = (value << 8) | reader.ReadUInt8();
        return value;
    }

    private static CffDictEntry[] ParseDict(byte[] data)
    {
        var entries = new List<CffDictEntry>();
        var operands = new List<double>(8);
        int index = 0;

        while (index < data.Length)
        {
            byte b0 = data[index];
            if (IsNumberToken(b0))
            {
                operands.Add(ReadDictNumber(data, ref index));
                continue;
            }

            ushort op = ReadDictOperator(data, ref index);
            entries.Add(new CffDictEntry(op, [.. operands]));
            operands.Clear();
        }

        return [.. entries];
    }

    private static bool IsNumberToken(byte value)
        => value == 28 || value == 29 || value == 30 || (value >= 32 && value <= 254);

    private static ushort ReadDictOperator(byte[] data, ref int index)
    {
        if (index >= data.Length)
            throw new EndOfStreamException();

        byte b0 = data[index++];
        if (b0 == 12)
        {
            if (index >= data.Length)
                throw new EndOfStreamException();

            return (ushort)(0x0C00 | data[index++]);
        }

        if (b0 <= 21 && b0 != 28)
            return b0;

        throw new InvalidDataException("Invalid CFF DICT operator encoding.");
    }

    private static double ReadDictNumber(byte[] data, ref int index)
    {
        byte b0 = data[index++];

        return b0 switch
        {
            >= 32 and <= 246 => b0 - 139,
            >= 247 and <= 250 => ((b0 - 247) * 256) + ReadRequiredByte(data, ref index) + 108,
            >= 251 and <= 254 => -((b0 - 251) * 256) - ReadRequiredByte(data, ref index) - 108,
            28 => ReadInt16(data, ref index),
            29 => ReadInt32(data, ref index),
            30 => ReadRealNumber(data, ref index),
            _ => throw new InvalidDataException("Invalid CFF DICT number encoding.")
        };
    }

    private static int ReadRequiredByte(byte[] data, ref int index)
    {
        if (index >= data.Length)
            throw new EndOfStreamException();
        return data[index++];
    }

    private static short ReadInt16(byte[] data, ref int index)
    {
        if (index + 2 > data.Length)
            throw new EndOfStreamException();

        short value = BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(index, 2));
        index += 2;
        return value;
    }

    private static int ReadInt32(byte[] data, ref int index)
    {
        if (index + 4 > data.Length)
            throw new EndOfStreamException();

        int value = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(index, 4));
        index += 4;
        return value;
    }

    private static double ReadRealNumber(byte[] data, ref int index)
    {
        var sb = new StringBuilder(16);

        while (true)
        {
            if (index >= data.Length)
                throw new EndOfStreamException();

            byte packed = data[index++];
            int high = packed >> 4;
            int low = packed & 0x0F;

            if (AppendRealNibble(sb, high) || AppendRealNibble(sb, low))
                break;
        }

        if (!double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            throw new InvalidDataException("Invalid CFF DICT real number.");

        return value;
    }

    private static bool AppendRealNibble(StringBuilder sb, int nibble)
    {
        switch (nibble)
        {
            case >= 0 and <= 9:
                sb.Append((char)('0' + nibble));
                return false;
            case 0xA:
                sb.Append('.');
                return false;
            case 0xB:
                sb.Append('E');
                return false;
            case 0xC:
                sb.Append("E-");
                return false;
            case 0xD:
                throw new InvalidDataException("Invalid CFF DICT real number nibble.");
            case 0xE:
                sb.Append('-');
                return false;
            case 0xF:
                return true;
            default:
                throw new InvalidDataException("Invalid CFF DICT real number nibble.");
        }
    }

    private static double GetSingleOperandOrDefault(CffDictEntry[] dict, ushort op, double defaultValue)
    {
        for (int i = dict.Length - 1; i >= 0; i--)
        {
            if (dict[i].Operator != op)
                continue;

            if (dict[i].Operands.Length != 1)
                throw new InvalidDataException("Invalid CFF DICT operator operand count.");

            return dict[i].Operands[0];
        }

        return defaultValue;
    }

    private static uint GetRequiredUnsignedOperand(CffDictEntry[] dict, ushort op)
    {
        for (int i = dict.Length - 1; i >= 0; i--)
        {
            if (dict[i].Operator != op)
                continue;

            if (dict[i].Operands.Length != 1)
                throw new InvalidDataException("Invalid CFF DICT operator operand count.");

            return ToUInt32(dict[i].Operands[0]);
        }

        throw new InvalidDataException("Missing required CFF DICT operator.");
    }

    private static uint GetOptionalUnsignedOperand(CffDictEntry[] dict, ushort op, uint defaultValue)
    {
        for (int i = dict.Length - 1; i >= 0; i--)
        {
            if (dict[i].Operator != op)
                continue;

            if (dict[i].Operands.Length != 1)
                throw new InvalidDataException("Invalid CFF DICT operator operand count.");

            return ToUInt32(dict[i].Operands[0]);
        }

        return defaultValue;
    }

    private static (uint Size, uint Offset) GetPrivateDictLocation(CffDictEntry[] topDict)
    {
        for (int i = topDict.Length - 1; i >= 0; i--)
        {
            if (topDict[i].Operator != TopDictPrivateOperator)
                continue;

            if (topDict[i].Operands.Length != 2)
                throw new InvalidDataException("Invalid Private DICT operands.");

            return (ToUInt32(topDict[i].Operands[0]), ToUInt32(topDict[i].Operands[1]));
        }

        return (0, 0);
    }

    private static uint ToUInt32(double value)
    {
        if (!double.IsFinite(value))
            throw new InvalidDataException("Invalid CFF numeric value.");

        double rounded = Math.Round(value);
        if (Math.Abs(rounded - value) > 1e-9 || rounded < 0 || rounded > uint.MaxValue)
            throw new InvalidDataException("Invalid CFF unsigned integer value.");

        return (uint)rounded;
    }

    private void ParseCharset(OpenTypeReader.TableScope scope, int glyphCount)
    {
        if (glyphCount <= 0)
        {
            CharsetKind = CffCharsetKind.None;
            CharsetSids = [];
            return;
        }

        if (CharsetOffset == 0)
        {
            CharsetKind = CffCharsetKind.IsoAdobe;
            CharsetSids = [];
            return;
        }

        if (CharsetOffset == 1)
        {
            CharsetKind = CffCharsetKind.Expert;
            CharsetSids = [];
            return;
        }

        if (CharsetOffset == 2)
        {
            CharsetKind = CffCharsetKind.ExpertSubset;
            CharsetSids = [];
            return;
        }

        using var charsetScope = scope.EnterScope(CharsetOffset);
        var reader = charsetScope.Reader;

        byte format = reader.ReadUInt8();
        var sids = new ushort[glyphCount];
        sids[0] = 0;

        int written = 1;
        switch (format)
        {
            case 0:
                CharsetKind = CffCharsetKind.Format0;
                while (written < glyphCount)
                    sids[written++] = reader.ReadUInt16();
                break;

            case 1:
                CharsetKind = CffCharsetKind.Format1;
                while (written < glyphCount)
                {
                    ushort first = reader.ReadUInt16();
                    int left = reader.ReadUInt8();
                    for (int i = 0; i <= left && written < glyphCount; i++)
                        sids[written++] = (ushort)(first + i);
                }
                break;

            case 2:
                CharsetKind = CffCharsetKind.Format2;
                while (written < glyphCount)
                {
                    ushort first = reader.ReadUInt16();
                    int left = reader.ReadUInt16();
                    for (int i = 0; i <= left && written < glyphCount; i++)
                        sids[written++] = (ushort)(first + i);
                }
                break;

            default:
                throw new InvalidDataException($"Unsupported CFF charset format '{format}'.");
        }

        CharsetSids = sids;
    }
}
