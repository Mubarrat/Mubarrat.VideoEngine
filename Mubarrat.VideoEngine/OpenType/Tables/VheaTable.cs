namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class VheaTable : IOpenTypeTable
{
    public string Tag => "vhea";

    public ushort MajorVersion { get; private set; }
    public ushort MinorVersion { get; private set; }

    // vhea 1.0 fields
    public short Ascent { get; private set; }
    public short Descent { get; private set; }
    public short LineGap { get; private set; }

    // vhea 1.1 fields (preferred names)
    public short VertTypoAscender { get; private set; }
    public short VertTypoDescender { get; private set; }
    public short VertTypoLineGap { get; private set; }

    public ushort AdvanceHeightMax { get; private set; }
    public short MinTopSideBearing { get; private set; }
    public short MinBottomSideBearing { get; private set; }
    public short YMaxExtent { get; private set; }

    public short CaretSlopeRise { get; private set; }
    public short CaretSlopeRun { get; private set; }
    public short CaretOffset { get; private set; }

    public short Reserved1 { get; private set; }
    public short Reserved2 { get; private set; }
    public short Reserved3 { get; private set; }
    public short Reserved4 { get; private set; }

    public short MetricDataFormat { get; private set; }
    public ushort NumOfLongVerMetrics { get; private set; }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scoped)
    {
        var reader = scoped.Reader;

        var version = reader.ReadUInt32();

        MajorVersion = (ushort)(version >> 16);
        MinorVersion = (ushort)(version & 0xFFFF);

        if (version == 0x00010000)
        {
            Ascent = reader.ReadInt16();
            Descent = reader.ReadInt16();
            LineGap = reader.ReadInt16();

            // map v1.0 → v1.1 style fields
            VertTypoAscender = Ascent;
            VertTypoDescender = Descent;
            VertTypoLineGap = LineGap;
        }
        else if (version == 0x00011000)
        {
            VertTypoAscender = reader.ReadInt16();
            VertTypoDescender = reader.ReadInt16();
            VertTypoLineGap = reader.ReadInt16();

            Ascent = VertTypoAscender;
            Descent = VertTypoDescender;
            LineGap = VertTypoLineGap;
        }
        else
        {
            throw new InvalidOperationException($"Unsupported vhea version: 0x{version:X8}");
        }

        AdvanceHeightMax = reader.ReadUInt16();
        MinTopSideBearing = reader.ReadInt16();
        MinBottomSideBearing = reader.ReadInt16();
        YMaxExtent = reader.ReadInt16();

        CaretSlopeRise = reader.ReadInt16();
        CaretSlopeRun = reader.ReadInt16();
        CaretOffset = reader.ReadInt16();

        Reserved1 = reader.ReadInt16();
        Reserved2 = reader.ReadInt16();
        Reserved3 = reader.ReadInt16();
        Reserved4 = reader.ReadInt16();

        MetricDataFormat = reader.ReadInt16();
        NumOfLongVerMetrics = reader.ReadUInt16();

        tables.Add(this);
    }
}
