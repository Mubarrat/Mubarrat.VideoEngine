namespace Mubarrat.OpenType.CommonTables;

public readonly record struct DeltaSetIndexMap(byte Format, byte EntryFormat, uint MapCount, byte[] MapData) : IOpenTypeCommonTable<DeltaSetIndexMap>
{
    public static DeltaSetIndexMap Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        var reader = scope.Reader;

        var format = reader.ReadUInt8();
        var entryFormat = reader.ReadUInt8();

        uint mapCount = format switch
        {
            0 => reader.ReadUInt16(),
            1 => reader.ReadUInt32(),
            _ => throw new InvalidDataException($"Invalid DeltaSetIndexMap format: {format}")
        };

        int entrySize = ((entryFormat & 0x30) >> 4) + 1;
        int totalBytes = checked((int)(entrySize * mapCount));

        var mapData = reader.ReadBytes(totalBytes);

        return new DeltaSetIndexMap(format, entryFormat, mapCount, mapData);
    }

    public (ushort outer, ushort inner) Map(int index)
    {
        if (MapData.Length == 0)
            return ((ushort)index, 0);

        if ((uint)index >= MapCount)
            index = (int)(MapCount - 1);

        int entrySize = ((EntryFormat & 0x30) >> 4) + 1;
        int offset = index * entrySize;

        uint entry = entrySize switch
        {
            1 => MapData[offset],
            2 => BitConverter.ToUInt16(MapData, offset),
            3 => (uint)(MapData[offset] | (MapData[offset + 1] << 8) | (MapData[offset + 2] << 16)),
            4 => BitConverter.ToUInt32(MapData, offset),
            _ => throw new InvalidDataException("Invalid entry size")
        };

        int innerBits = (EntryFormat & 0x0F) + 1;

        ushort outer = (ushort)(entry >> innerBits);
        ushort inner = (ushort)(entry & ((1u << innerBits) - 1));

        return (outer, inner);
    }
}
