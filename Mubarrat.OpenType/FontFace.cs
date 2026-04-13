using Mubarrat.OpenType.Tables;
using System.Buffers.Binary;

namespace Mubarrat.OpenType;

public sealed class FontFace
{
    public string Key { get; }

    public ParsedTables Tables { get; }

    internal FontFace(string key, OpenTypeReader reader)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));

        Tables = new(reader);

        uint sfntVersion = reader.ReadUInt32();
        ushort numTables = reader.ReadUInt16();
        ushort searchRange = reader.ReadUInt16();
        ushort entrySelector = reader.ReadUInt16();
        ushort rangeShift = reader.ReadUInt16();

        for (int i = 0; i < numTables; i++)
        {
            string tag = reader.ReadTag();
            uint checkSum = reader.ReadUInt32();
            uint offset = reader.ReadUInt32();
            uint length = reader.ReadUInt32();
            using (var scope = reader.EnterScope(offset))
            {
                byte[] tableBytes = reader.ReadBytes((int)length);

                if (tag == "head")
                    tableBytes[8] = tableBytes[9] = tableBytes[10] = tableBytes[11] = 0;

                uint computed = CalcTableChecksum(tableBytes);

                if (computed != checkSum)
                    throw new InvalidDataException(
                        $"Checksum mismatch for table '{tag}': expected {checkSum:X8}, got {computed:X8}");

                scope.Seek(0);

                IOpenTypeTable.GetEmptyTableFromTag(tag)?.Parse(Tables, scope);
            }
        }
    }

    public static uint CalcTableChecksum(ReadOnlySpan<byte> table)
    {
        uint sum = 0;

        int length = (table.Length + 3) & ~3; // round up to multiple of 4

        for (int i = 0; i < length; i += 4)
        {
            uint word = 0;

            int remaining = table.Length - i;
            if (remaining >= 4)
            {
                word = BinaryPrimitives.ReadUInt32BigEndian(table.Slice(i, 4));
            }
            else
            {
                Span<byte> temp = stackalloc byte[4];
                table[i..].CopyTo(temp);
                word = BinaryPrimitives.ReadUInt32BigEndian(temp);
            }

            unchecked
            {
                sum += word;
            }
        }

        return sum;
    }
}
