namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct SbixStrikeRecord(ushort Ppem, ushort Ppi, uint[] GlyphDataOffsets, SbixGlyphDataRecord[] Glyphs);

public readonly record struct SbixGlyphDataRecord(
    short OriginOffsetX,
    short OriginOffsetY,
    string GraphicType,
    byte[] Data,
    ushort? DupeGlyphId);
