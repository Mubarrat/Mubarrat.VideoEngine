using Mubarrat.OpenType;
using Mubarrat.OpenType.Tables;
using Mubarrat.VideoEngine.Immutable;
using Mubarrat.VideoEngine.Path;

namespace Mubarrat.VideoEngine.Latex;

internal static class Helpers
{
    public static void GetVerticalGlyphInfo(char character, FontMetrics metrics, double targetHeight, out double finalWidth, out double finalHeight)
    {
        finalWidth = double.NaN;
        finalHeight = double.NaN;

        if (!metrics.Face.Tables.TryGet(out CmapTable cmap) || !cmap.TryGetGlyphId(character, out ushort glyphId))
            return;

        if (!metrics.Face.Tables.TryGet(out MathTable math) || !math.MathVariants.VerticalGlyphCoverage.TryGetIndex(glyphId, out ushort index))
        {
            (finalWidth, finalHeight) = metrics.GetAdvanceWidthAndHeight(glyphId);
            return;
        }

        ref var constructions = ref math.MathVariants.VerticalConstructions[index];

        foreach (ref var variant in constructions.Variants.AsSpan())
            if ((finalHeight = variant.AdvanceMeasurement * metrics.Scale) >= targetHeight)
            {
                finalWidth = metrics.GetAdvanceWidth(variant.VariantGlyph);
                return;
            }

        if (!GetVerticalGlyphAssembly(metrics, index, targetHeight, ref finalWidth, ref finalHeight).Any())
            finalWidth = metrics.GetAdvanceWidth(glyphId);
    }

    private static IEnumerable<MathTable.GlyphPart> GetVerticalGlyphAssembly(FontMetrics metrics, ushort index, double targetHeight, ref double finalWidth, ref double finalHeight)
    {
        IEnumerable<MathTable.GlyphPart> finalParts = [];

        if (metrics.Face.Tables.TryGet(out MathTable math) &&
            math.MathVariants.VerticalConstructions[index].GlyphAssembly is { PartRecords: { Length: > 0 } parts })
        {
            if (metrics.Face.Tables.TryGet(out HmtxTable hmtx1))
                finalWidth = parts.Max(x => hmtx1.GetAdvanceWidth(x.GlyphId)) * metrics.Scale;

            int fixedCount = 0, extenderCount = 0;
            double fixedAdvance = 0, extenderAdvance = 0;
            foreach (ref var part in parts.AsSpan()) // Glyph part by reference to avoid copying the struct multiple times
            {
                if (part.PartFlags.HasFlag(MathTable.GlyphPartFlags.Extender))
                {
                    extenderCount++;
                    extenderAdvance += part.FullAdvance;
                }
                else
                {
                    fixedCount++;
                    fixedAdvance += part.FullAdvance;
                }
            }
            fixedAdvance *= metrics.Scale;
            extenderAdvance *= metrics.Scale;

            double o = math.MathVariants.MinConnectorOverlap * metrics.Scale;

            double baseHeight = fixedAdvance - o * (fixedCount - 1);
            double gain = extenderAdvance - o * extenderCount;
            double target = targetHeight;
            if (gain <= 0)
            {
                finalHeight = baseHeight;
                return finalParts;
            }
            if (baseHeight >= target)
            {
                finalHeight = targetHeight;
                return finalParts;
            }
            int k = (int)Math.Ceiling((target - baseHeight) / gain);
            finalParts = parts.SelectMany(p => p.PartFlags.HasFlag(MathTable.GlyphPartFlags.Extender) ? Enumerable.Repeat(p, k) : [p]);

            double maximumHeight = baseHeight + k * gain;
            double minimumHeight = SumWithMaximumOverlap(parts) * metrics.Scale;
            finalHeight = Math.Clamp(targetHeight, minimumHeight, maximumHeight);
        }
        return finalParts;
    }

    public static Path2D GetVerticalGlyph(char character, FontMetrics metrics, double targetHeight)
    {
        if (!metrics.Face.Tables.TryGet(out CmapTable cmap) || !cmap.TryGetGlyphId(character, out ushort glyphId))
            return new();

        if (!metrics.Face.Tables.TryGet(out MathTable math) || !math.MathVariants.VerticalGlyphCoverage.TryGetIndex(glyphId, out ushort index))
            return metrics.Face.ToPath2D(glyphId, metrics.FontSize, false);


        ref var constructions = ref math.MathVariants.VerticalConstructions[index];

        foreach (ref var variant in constructions.Variants.AsSpan())
        {
            if (variant.AdvanceMeasurement * metrics.Scale >= targetHeight)
                return metrics.Face.ToPath2D(variant.VariantGlyph, metrics.FontSize, false);
        }

        var _ = 0d;
        double finalHeight = 0;
        IEnumerable<MathTable.GlyphPart> enumerable = GetVerticalGlyphAssembly(metrics, index, targetHeight, ref _, ref finalHeight);
        if (enumerable.Any())
        {
            var parts = enumerable.ToArray();
            Array.Reverse(parts);
            var paths = Array.ConvertAll(parts, p => metrics.Face.ToPath2D(p.GlyphId, metrics.FontSize, false));
            var totalHeight = paths.Sum(x => x.Bounds.Height);
            var toCollapsePerPart = (totalHeight - finalHeight) / (parts.Length - 1);
            double y = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                paths[i] *= Matrix2D.Translate(0, y - paths[i].Bounds.Y);
                y += paths[i].Bounds.Height - toCollapsePerPart;
            }
            return Path2D.Combine(paths);
        }

        return new();
    }

    // smallest possible size
    public static double SumWithMaximumOverlap(IEnumerable<MathTable.GlyphPart> parts)
    {
        double sum = 0;
        MathTable.GlyphPart? last = null;
        foreach (var part in parts)
        {
            sum += part.FullAdvance;
            if (last is { } lp)
                sum -= Math.Min(lp.EndConnectorLength, part.StartConnectorLength);
            last = part;
        }
        return sum;
    }

    // largest possible size
    public static double SumWithMinimumOverlap(IEnumerable<MathTable.GlyphPart> parts, int count, double minimumOverlap)
        => parts.Sum(x => x.FullAdvance) - Math.Max(0, count - 1) * minimumOverlap;
}
