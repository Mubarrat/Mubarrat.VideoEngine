using HarfBuzzSharp;
using System.Collections.Concurrent;

namespace Mubarrat.OpenType.TextShaping;

public static partial class OpenTypeTextShaper
{
    public static bool EnableHarfBuzzShaping { get; set; } = true;

    private static bool TryShapeWithHarfBuzz(
        string text,
        FontMetrics metrics,
        OpenTypeShapingOptions options,
        out ShapingResult result)
    {
        result = default;

        if (!EnableHarfBuzzShaping)
            return false;

        if (text.Length == 0)
        {
            result = new ShapingResult([], 0, TextShaperScript.Unknown, false);
            return true;
        }

        Face face = GetOrCreateFace(metrics.Face);

        using Font font = CreateFont(face);

        var runes = ParseRunes(text);
        var runs = ItemizeRuns(runes, options.RightToLeft);

        var shaped = new List<ShapedGlyph>(Math.Max(text.Length, 8));
        double width = 0;

        Feature[] features = BuildHarfBuzzFeatures(
            BuildEnabledFeatures(TextShaperScript.Unknown, options));

        for (int runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];
            bool rtl = options.RightToLeft ?? ((run.BidiLevel & 1) == 1);

            GetUtf16RangeFromRuneRange(text, run.Start, run.Length,
                out int utf16Start, out int utf16End);

            string runText = text[utf16Start..utf16End];

            using HarfBuzzSharp.Buffer buffer = new();

            buffer.AddUtf16(runText);
            buffer.Direction = rtl ? Direction.RightToLeft : Direction.LeftToRight;
            buffer.Script = MapToHarfBuzzScript(run.Script);

            if (!string.IsNullOrWhiteSpace(options.LanguageTag))
                buffer.Language = new Language(options.LanguageTag);
            else
                buffer.GuessSegmentProperties();

            font.Shape(buffer, features);

            var infos = buffer.GetGlyphInfoSpan();
            var positions = buffer.GetGlyphPositionSpan();

            double minX = 0;
            double maxX = 0;

            double cursorX = 0;

            for (int i = 0; i < infos.Length; i++)
            {
                double xAdvance = positions[i].XAdvance * metrics.Scale;

                double x = cursorX + positions[i].XOffset * metrics.Scale;

                double left = x;
                double right = x + xAdvance;

                minX = Math.Min(minX, left);
                maxX = Math.Max(maxX, right);

                shaped.Add(new ShapedGlyph(
                    Face: metrics.Face,
                    GlyphId: (ushort)Math.Min(infos[i].Codepoint, ushort.MaxValue),
                    CodePoint: 0,
                    Cluster: utf16Start + (int)infos[i].Cluster,
                    XAdvance: xAdvance,
                    YAdvance: positions[i].YAdvance * metrics.Scale,
                    XOffset: positions[i].XOffset * metrics.Scale,
                    YOffset: positions[i].YOffset * metrics.Scale));

                cursorX += xAdvance;
            }

            width = maxX - minX;
        }

        bool singleRun = runs.Count == 1;
        var script = singleRun ? runs[0].Script : TextShaperScript.Unknown;
        bool rtlFinal = singleRun
            ? ((runs[0].BidiLevel & 1) == 1)
            : options.RightToLeft == true;

        result = new ShapingResult(shaped, width, script, rtlFinal);
        return true;
    }

    private static unsafe Face GetOrCreateFace(FontFace face) => new((_, tag) =>
    {
        if (!face.TryGetShapingTableBlob(tag.ToString().ToLowerInvariant(), out var bytes))
            return Blob.Empty;
        fixed (byte* ptr = bytes.Span)
            return new((nint)ptr, bytes.Length, MemoryMode.ReadOnly); // Read-only since the underlying memory is immutable and shared across threads
    });

    private static Font CreateFont(Face face)
    {
        var font = new Font(face);

        font.SetFunctionsOpenType();
        font.SetScale(face.UnitsPerEm, face.UnitsPerEm);

        return font;
    }

    private static Feature[] BuildHarfBuzzFeatures(IReadOnlyList<string> enabledFeatures)
    {
        var features = new List<Feature>(enabledFeatures.Count);

        for (int i = 0; i < enabledFeatures.Count; i++)
        {
            string tag = enabledFeatures[i];

            if (tag.Length != 4)
                continue;

            features.Add(new Feature(new Tag(tag[0], tag[1], tag[2], tag[3]), 1));
        }

        return [.. features];
    }

    private static Script MapToHarfBuzzScript(TextShaperScript script) => script switch
    {
        TextShaperScript.Latin => Script.Latin,
        TextShaperScript.Arabic => Script.Arabic,
        TextShaperScript.Devanagari => Script.Devanagari,
        TextShaperScript.Bengali => Script.Bengali,
        TextShaperScript.Gurmukhi => Script.Gurmukhi,
        TextShaperScript.Gujarati => Script.Gujarati,
        TextShaperScript.Oriya => Script.Oriya,
        TextShaperScript.Tamil => Script.Tamil,
        TextShaperScript.Telugu => Script.Telugu,
        TextShaperScript.Kannada => Script.Kannada,
        TextShaperScript.Malayalam => Script.Malayalam,
        TextShaperScript.Sinhala => Script.Sinhala,
        TextShaperScript.Khmer => Script.Khmer,
        TextShaperScript.Thai => Script.Thai,
        TextShaperScript.Lao => Script.Lao,
        TextShaperScript.Myanmar => Script.Myanmar,
        TextShaperScript.Hebrew => Script.Hebrew,
        TextShaperScript.Syriac => Script.Syriac,
        TextShaperScript.Tibetan => Script.Tibetan,
        TextShaperScript.Hangul => Script.Hangul,
        _ => Script.Unknown
    };
}
