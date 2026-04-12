namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct SbitLineMetrics(
    sbyte Ascender,
    sbyte Descender,
    byte WidthMax,
    sbyte CaretSlopeNumerator,
    sbyte CaretSlopeDenominator,
    sbyte CaretOffset,
    sbyte MinOriginSb,
    sbyte MinAdvanceSb,
    sbyte MaxBeforeBl,
    sbyte MinAfterBl,
    sbyte Pad1,
    sbyte Pad2)
{
    public static SbitLineMetrics Read(OpenTypeReader reader) => new(
        reader.ReadInt8(),
        reader.ReadInt8(),
        reader.ReadUInt8(),
        reader.ReadInt8(),
        reader.ReadInt8(),
        reader.ReadInt8(),
        reader.ReadInt8(),
        reader.ReadInt8(),
        reader.ReadInt8(),
        reader.ReadInt8(),
        reader.ReadInt8(),
        reader.ReadInt8());
}

public readonly record struct EmbeddedBitmapSizeRecord(
    uint IndexSubtableListOffset,
    uint IndexSubtableListSize,
    uint NumberOfIndexSubtables,
    uint ColorRef,
    SbitLineMetrics HorizontalMetrics,
    SbitLineMetrics VerticalMetrics,
    ushort StartGlyphIndex,
    ushort EndGlyphIndex,
    byte PpemX,
    byte PpemY,
    byte BitDepth,
    sbyte Flags)
{
    public static EmbeddedBitmapSizeRecord Read(OpenTypeReader reader) => new(
        reader.ReadOffset32(),
        reader.ReadUInt32(),
        reader.ReadUInt32(),
        reader.ReadUInt32(),
        SbitLineMetrics.Read(reader),
        SbitLineMetrics.Read(reader),
        reader.ReadUInt16(),
        reader.ReadUInt16(),
        reader.ReadUInt8(),
        reader.ReadUInt8(),
        reader.ReadUInt8(),
        reader.ReadInt8());
}
