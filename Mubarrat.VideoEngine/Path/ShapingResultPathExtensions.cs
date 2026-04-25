using Mubarrat.OpenType;
using Mubarrat.OpenType.Tables;
using Mubarrat.OpenType.TextShaping;
using Mubarrat.VideoEngine.Immutable;
using System.Numerics;

namespace Mubarrat.VideoEngine.Path;

public static class ShapingResultPathExtensions
{
    /// <summary>
    /// Converts a shaped text run into a <see cref="PathBuilder"/> using the glyph outlines from the font.
    /// </summary>
    public static PathBuilder ToPathBuilder(this ShapingResult shapingResult, double fontSize, Point origin, bool flipY = true)
    {
        ArgumentNullException.ThrowIfNull(shapingResult);

        var builder = new PathBuilder();
        double penX = origin.X;
        double penY = origin.Y;
        double ppem = fontSize;

        for (int i = 0; i < shapingResult.Glyphs.Count; i++)
        {
            var glyph = shapingResult.Glyphs[i];
            double glyphOriginX = penX + glyph.XOffset;
            double glyphOriginY = penY + glyph.YOffset;

            glyph.Face.Tables.TryGet(out HeadTable head);
            bool hasGlyf = glyph.Face.Tables.TryGet(out GlyfTable glyf);
            bool hasCff = glyph.Face.Tables.TryGet(out CffTable cff);

            if (!hasGlyf && !hasCff)
                throw new InvalidOperationException("The font does not contain supported outlines (glyf or CFF). ");

            double scale = fontSize / head.UnitsPerEm;

            if (hasGlyf && glyph.GlyphId < glyf.Glyphs.Length)
            {
                var contours = glyf.GetContours(glyph.GlyphId);

                for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++)
                {
                    var contour = contours[contourIndex].Points;
                    AppendContour(builder, contour, glyphOriginX, glyphOriginY, scale, flipY);
                }
            }
            else if (hasCff && glyph.GlyphId < cff.CharStrings.Length)
            {
                AppendCffGlyph(builder, cff, glyph.GlyphId, glyphOriginX, glyphOriginY, scale, flipY);
            }

            penX += glyph.XAdvance;
            penY += glyph.YAdvance;
        }

        return builder;
    }

    /// <summary>
    /// Converts a shaped text run into a <see cref="PathBuilder"/> using origin (0,0).
    /// </summary>
    public static PathBuilder ToPathBuilder(this ShapingResult shapingResult, double fontSize, bool flipY = true)
        => shapingResult.ToPathBuilder(fontSize, Point.Zero, flipY);

    /// <summary>
    /// Converts a shaped text run to <see cref="Path2D"/> through <see cref="PathBuilder"/>.
    /// </summary>
    public static Path2D ToPath2D(this ShapingResult shapingResult, double fontSize, Point origin, bool isNonZeroFill = true, bool flipY = true)
        => shapingResult.ToPathBuilder(fontSize, origin, flipY).Build(isNonZeroFill);

    /// <summary>
    /// Converts a shaped text run to <see cref="Path2D"/> through <see cref="PathBuilder"/> using origin (0,0).
    /// </summary>
    public static Path2D ToPath2D(this ShapingResult shapingResult, double fontSize, bool isNonZeroFill = true, bool flipY = true)
        => shapingResult.ToPathBuilder(fontSize, Point.Zero, flipY).Build(isNonZeroFill);

    private static void AppendContour(
        PathBuilder builder,
        GlyfTable.GlyphOutlinePoint[] contour,
        double glyphOriginX,
        double glyphOriginY,
        double scale,
        bool flipY)
    {
        if (contour is null || contour.Length == 0)
            return;
        static Vector2 Midpoint(in Vector2 a, in Vector2 b) => (a + b) * 0.5f;
        Point Transform(in Vector2 position) => new(
            glyphOriginX + (position.X * scale),
            glyphOriginY + (position.Y * (flipY ? -scale : scale)));
        static bool NearlyEqual(in Vector2 a, in Vector2 b)
            => MathF.Abs(a.X - b.X) <= 1e-6f && MathF.Abs(a.Y - b.Y) <= 1e-6f;
        int startIndex = FindStartIndex(contour);
        Vector2 startPoint = GetStartPoint(contour, startIndex);
        builder.MoveTo(Transform(startPoint));
        Vector2? pendingControl = null;
        for (int step = 1; step <= contour.Length; step++)
        {
            int index = (startIndex + step) % contour.Length;
            var point = contour[index];
            if (point.OnCurve)
            {
                if (pendingControl is { } control)
                {
                    builder.QuadraticTo(Transform(control), Transform(point.Position));
                    pendingControl = null;
                }
                else
                {
                    if (step != contour.Length || !NearlyEqual(point.Position, startPoint))
                        builder.LineTo(Transform(point.Position));
                }
                continue;
            }
            if (pendingControl is { } previousControl)
                builder.QuadraticTo(Transform(previousControl), Transform(Midpoint(previousControl, point.Position)));
            pendingControl = point.Position;
        }
        if (pendingControl is { } lastControl)
            builder.QuadraticTo(Transform(lastControl), Transform(startPoint));
        builder.Close();
    }

    private static int FindStartIndex(GlyfTable.GlyphOutlinePoint[] contour)
    {
        for (int i = 0; i < contour.Length; i++)
            if (contour[i].OnCurve)
                return i;
        return 0;
    }

    private static Vector2 GetStartPoint(GlyfTable.GlyphOutlinePoint[] contour, int startIndex)
    {
        var start = contour[startIndex];
        return start.OnCurve ? start.Position : (contour[(startIndex - 1 + contour.Length) % contour.Length].Position + start.Position) * 0.5f;
    }

    private static void AppendCffGlyph(PathBuilder builder, CffTable cff, ushort glyphId, double glyphOriginX, double glyphOriginY, double scale, bool flipY)
    {
        Type2BuildState state = new(builder, glyphOriginX, glyphOriginY, scale, flipY);
        Type2IlCompiler.CompileGlyph(cff, glyphId)(state);
        state.EnsureClosed();
    }

    public static Path2D ToPath2D(
        this FontFace face,
        ushort glyphId,
        double fontSize,
        Point origin,
        bool isNonZeroFill = true,
        bool flipY = true)
    {
        var builder = new PathBuilder();

        if (!face.Tables.TryGet(out HeadTable head))
            throw new InvalidOperationException("Font missing head table.");

        bool hasGlyf = face.Tables.TryGet(out GlyfTable glyf);
        bool hasCff = face.Tables.TryGet(out CffTable cff);

        if (!hasGlyf && !hasCff)
            throw new InvalidOperationException("The font does not contain supported outlines (glyf or CFF).");

        double scale = fontSize / head.UnitsPerEm;

        if (hasGlyf && glyphId < glyf.Glyphs.Length)
        {
            var contours = glyf.GetContours(glyphId);

            for (int i = 0; i < contours.Length; i++)
            {
                AppendContour(
                    builder,
                    contours[i].Points,
                    origin.X,
                    origin.Y,
                    scale,
                    flipY);
            }
        }
        else if (hasCff && glyphId < cff.CharStrings.Length)
        {
            AppendCffGlyph(builder, cff, glyphId, origin.X, origin.Y, scale, flipY);
        }

        return builder.Build(isNonZeroFill);
    }

    public static Path2D ToPath2D(
        this FontFace face,
        ushort glyphId,
        double fontSize,
        bool isNonZeroFill = true,
        bool flipY = true)
        => face.ToPath2D(glyphId, fontSize, Point.Zero, isNonZeroFill, flipY);
}
