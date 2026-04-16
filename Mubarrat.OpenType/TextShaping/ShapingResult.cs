namespace Mubarrat.OpenType.TextShaping;

public sealed record ShapingResult(
    IReadOnlyList<ShapedGlyph> Glyphs,
    double Width,
    TextShaperScript Script,
    bool RightToLeft);
