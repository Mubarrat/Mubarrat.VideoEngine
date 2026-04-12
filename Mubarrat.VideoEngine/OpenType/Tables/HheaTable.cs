namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class HheaTable : IOpenTypeTable
{
    public ushort MajorVersion { get; private set; }
    public ushort MinorVersion { get; private set; }

    public short Ascender { get; private set; }
    public short Descender { get; private set; }
    public short LineGap { get; private set; }

    public ushort AdvanceWidthMax { get; private set; }

    public short MinLeftSideBearing { get; private set; }
    public short MinRightSideBearing { get; private set; }
    public short XMaxExtent { get; private set; }

    public short CaretSlopeRise { get; private set; }
    public short CaretSlopeRun { get; private set; }
    public short CaretOffset { get; private set; }

    public short Reserved1 { get; private set; }
    public short Reserved2 { get; private set; }
    public short Reserved3 { get; private set; }
    public short Reserved4 { get; private set; }

    public short MetricDataFormat { get; private set; }
    public ushort NumberOfHMetrics { get; private set; }

    public string Tag => "hhea";

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scoped)
    {
        var reader = scoped.Reader;

        MajorVersion = reader.ReadUInt16();
        MinorVersion = reader.ReadUInt16();

        Ascender = reader.ReadInt16();
        Descender = reader.ReadInt16();
        LineGap = reader.ReadInt16();

        AdvanceWidthMax = reader.ReadUInt16();

        MinLeftSideBearing = reader.ReadInt16();
        MinRightSideBearing = reader.ReadInt16();
        XMaxExtent = reader.ReadInt16();

        CaretSlopeRise = reader.ReadInt16();
        CaretSlopeRun = reader.ReadInt16();
        CaretOffset = reader.ReadInt16();

        Reserved1 = reader.ReadInt16();
        Reserved2 = reader.ReadInt16();
        Reserved3 = reader.ReadInt16();
        Reserved4 = reader.ReadInt16();

        MetricDataFormat = reader.ReadInt16();
        NumberOfHMetrics = reader.ReadUInt16();

        tables.Add(this);
    }
}
