using Mubarrat.VideoEngine.OpenType.Tables;

namespace Mubarrat.VideoEngine.OpenType;

public readonly record struct FontMetrics(
    FontFace Face,
    double FontSize,
    double Scale,
    double LineHeight,
    double Ascent,
    double Descent,
    double LineGap
)
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
}
