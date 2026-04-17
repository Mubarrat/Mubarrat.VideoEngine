using HarfBuzzSharp;
using System.Collections.Concurrent;

namespace Mubarrat.OpenType.TextShaping;

public static partial class OpenTypeTextShaper
{
    private static readonly ConcurrentDictionary<string, HarfBuzzFaceCacheEntry> harfBuzzFaceCache = new(StringComparer.Ordinal);

    private sealed class HarfBuzzFaceCacheEntry
    {
        public required Face Face { get; init; }
        public required IReadOnlyDictionary<string, Blob> TableBlobs { get; init; }
    }

    private static bool TryShapeWithHarfBuzz(string text, FontMetrics metrics, OpenTypeShapingOptions options, out ShapingResult result)
    {
        result = default;

        if (text.Length == 0)
        {
            result = new ShapingResult([], 0, TextShaperScript.Unknown, false);
            return true;
        }

        OpenTypeShapingTableBlobs tableBlobs = OpenTypeShapingTableBlobs.FromFace(metrics.Face);
        if (tableBlobs.Cmap.IsEmpty || tableBlobs.Head.IsEmpty || tableBlobs.Hhea.IsEmpty || tableBlobs.Hmtx.IsEmpty)
            return false;

        HarfBuzzFaceCacheEntry faceEntry = GetOrCreateHarfBuzzFace(metrics.Face.Key, tableBlobs);

        using var font = new Font(faceEntry.Face);
        font.SetFunctionsOpenType();
        font.SetScale((int)faceEntry.Face.UnitsPerEm, (int)faceEntry.Face.UnitsPerEm);

        var runes = ParseRunes(text);
        var runs = ItemizeRuns(runes, options.RightToLeft);

        var shaped = new List<ShapedGlyph>(Math.Max(text.Length, 8));
        double width = 0;

        Feature[] features = BuildHarfBuzzFeatures(BuildEnabledFeatures(TextShaperScript.Unknown, options));

        for (int runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];
            bool runRightToLeft = options.RightToLeft ?? ((run.BidiLevel & 1) == 1);

            GetUtf16RangeFromRuneRange(text, run.Start, run.Length, out int utf16Start, out int utf16End);
            string runText = text.Substring(utf16Start, utf16End - utf16Start);

            using var buffer = new HarfBuzzSharp.Buffer();
            buffer.AddUtf16(runText);
            buffer.Direction = runRightToLeft ? Direction.RightToLeft : Direction.LeftToRight;
            buffer.Script = MapToHarfBuzzScript(run.Script);
            if (!string.IsNullOrWhiteSpace(options.LanguageTag))
                buffer.Language = new Language(options.LanguageTag);
            else
                buffer.GuessSegmentProperties();

            font.Shape(buffer, features);

            var infos = buffer.GetGlyphInfoSpan();
            var positions = buffer.GetGlyphPositionSpan();

            for (int i = 0; i < infos.Length; i++)
            {
                double xAdvance = positions[i].XAdvance * metrics.Scale;
                shaped.Add(new ShapedGlyph(
                    Face: metrics.Face,
                    GlyphId: (ushort)Math.Min(infos[i].Codepoint, ushort.MaxValue),
                    CodePoint: 0,
                    Cluster: utf16Start + (int)infos[i].Cluster,
                    XAdvance: xAdvance,
                    YAdvance: positions[i].YAdvance * metrics.Scale,
                    XOffset: positions[i].XOffset * metrics.Scale,
                    YOffset: positions[i].YOffset * metrics.Scale));

                width += xAdvance;
            }
        }

        bool singleRun = runs.Count == 1;
        var resultScript = singleRun ? runs[0].Script : TextShaperScript.Unknown;
        bool rtl = (singleRun && ((runs[0].BidiLevel & 1) == 1)) || options.RightToLeft == true;

        result = new ShapingResult(shaped, width, resultScript, rtl);
        return true;
    }

    private static HarfBuzzFaceCacheEntry GetOrCreateHarfBuzzFace(string faceKey, OpenTypeShapingTableBlobs tableBlobs)
    {
        return harfBuzzFaceCache.GetOrAdd(faceKey, _ =>
        {
            var blobs = new Dictionary<string, Blob>(StringComparer.Ordinal)
            {
                ["cmap"] = Blob.FromStream(new MemoryStream(tableBlobs.Cmap.ToArray(), writable: false)),
                ["head"] = Blob.FromStream(new MemoryStream(tableBlobs.Head.ToArray(), writable: false)),
                ["hhea"] = Blob.FromStream(new MemoryStream(tableBlobs.Hhea.ToArray(), writable: false)),
                ["hmtx"] = Blob.FromStream(new MemoryStream(tableBlobs.Hmtx.ToArray(), writable: false))
            };

            if (!tableBlobs.Gsub.IsEmpty)
                blobs["gsub"] = Blob.FromStream(new MemoryStream(tableBlobs.Gsub.ToArray(), writable: false));
            if (!tableBlobs.Gpos.IsEmpty)
                blobs["gpos"] = Blob.FromStream(new MemoryStream(tableBlobs.Gpos.ToArray(), writable: false));
            if (!tableBlobs.Gdef.IsEmpty)
                blobs["gdef"] = Blob.FromStream(new MemoryStream(tableBlobs.Gdef.ToArray(), writable: false));
            if (!tableBlobs.Kern.IsEmpty)
                blobs["kern"] = Blob.FromStream(new MemoryStream(tableBlobs.Kern.ToArray(), writable: false));

            var face = new Face((_, tag) =>
            {
                string key = tag.ToString().ToLowerInvariant();
                if (blobs.TryGetValue(key, out var blob))
                    return blob;
                return Blob.Empty;
            });

            return new HarfBuzzFaceCacheEntry
            {
                Face = face,
                TableBlobs = blobs
            };
        });
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
