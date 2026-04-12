namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct ColrBaseGlyphRecordV0(ushort GlyphId, ushort FirstLayerIndex, ushort NumLayers);
public readonly record struct ColrLayerRecordV0(ushort GlyphId, ushort PaletteIndex);
public readonly record struct ColrBaseGlyphPaintRecordV1(ushort GlyphId, uint PaintOffset);

public readonly record struct CpalColorRecord(byte Blue, byte Green, byte Red, byte Alpha)
{
    public static CpalColorRecord Read(OpenTypeReader reader) => new(
        reader.ReadUInt8(),
        reader.ReadUInt8(),
        reader.ReadUInt8(),
        reader.ReadUInt8());
}
