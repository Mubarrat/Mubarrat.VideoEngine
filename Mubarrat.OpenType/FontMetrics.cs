using Mubarrat.OpenType.Tables;

namespace Mubarrat.OpenType;

public readonly record struct FontMetrics(
    FontFace Face,
    double FontSize,
    double Scale,
    double LineHeight,
    double Ascent,
    double Descent,
    double LineGap)
{
    public static FontMetrics Create(FontFace face, double fontSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fontSize);

        face.Tables.TryGet(out HeadTable head);

        // unitsPerEm is the key scaling factor
        double scale = fontSize / head.UnitsPerEm;

        face.Tables.TryGet(out HheaTable hhea);

        double ascent = hhea.Ascender * scale;
        double descent = hhea.Descender * scale;
        double lineGap = hhea.LineGap * scale;

        double lineHeight = ascent - descent + lineGap;

        return new FontMetrics(
            Face: face,
            FontSize: fontSize,
            Scale: scale,
            LineHeight: lineHeight,
            Ascent: ascent,
            Descent: descent,
            LineGap: lineGap
        );
    }

    public double GetAdvanceWidth(ushort glyphId) => Face.Tables.TryGet(out HmtxTable hmtx) ? hmtx.GetAdvanceWidth(glyphId) * Scale : double.NaN;

    public double GetAdvanceHeight(ushort glyphId) => Face.Tables.TryGet(out VmtxTable vmtx) ? vmtx.GetGlyphMetrics(glyphId).AdvanceHeight * Scale : double.NaN;

    public (double, double) GetAdvanceWidthAndHeight(ushort glyphId) => (GetAdvanceWidth(glyphId), GetAdvanceHeight(glyphId));
}
