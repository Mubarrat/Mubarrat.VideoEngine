namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class AvarTable : IOpenTypeTable
{
    public const string TableTag = "avar";

    public string Tag => TableTag;

    public HeaderInfo Header { get; private set; } = default;
    public SegmentMap[] SegmentMaps { get; private set; } = [];

    public readonly record struct HeaderInfo(ushort MajorVersion, ushort MinorVersion, ushort AxisCount);

    public readonly record struct AxisValueMap(float FromCoordinate, float ToCoordinate);

    public readonly record struct SegmentMap(AxisValueMap[] AxisValueMaps);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        ushort majorVersion = scope.Reader.ReadUInt16();
        ushort minorVersion = scope.Reader.ReadUInt16();
        scope.Reader.ReadUInt16(); // reserved
        ushort axisCount = scope.Reader.ReadUInt16();

        if (majorVersion != 1)
            throw new InvalidDataException($"Unsupported avar major version {majorVersion}.");

        Header = new HeaderInfo(majorVersion, minorVersion, axisCount);

        SegmentMaps = new SegmentMap[axisCount];
        for (int axisIndex = 0; axisIndex < SegmentMaps.Length; axisIndex++)
        {
            ushort positionMapCount = scope.Reader.ReadUInt16();
            var maps = new AxisValueMap[positionMapCount];
            for (int i = 0; i < maps.Length; i++)
                maps[i] = new AxisValueMap(scope.Reader.ReadF2Dot14(), scope.Reader.ReadF2Dot14());

            SegmentMaps[axisIndex] = new SegmentMap(maps);
        }

        tables.Add(this);
    }
}
