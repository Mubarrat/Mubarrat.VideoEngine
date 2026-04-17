namespace Mubarrat.OpenType.TextShaping;

public readonly record struct ShapedGlyph(
    FontFace Face,
    ushort GlyphId,
    uint CodePoint,
    int Cluster,
    double XAdvance,
    double YAdvance,
    double XOffset,
    double YOffset);
