using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.Tables;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Mubarrat.OpenType.TextShaping;

public static partial class OpenTypeTextShaper
{
    private const int DefaultMaxContextLookupRecursion = 8;

    private static readonly ConcurrentDictionary<LookupPlanCacheKey, ushort[]> lookupPlanCache = new();

    [ThreadStatic]
    private static LookupFilterContext? currentLookupFilterContext;

    [ThreadStatic]
    private static VariationInstance? currentVariationInstance;

    [ThreadStatic]
    private static ItemVariationStore? currentItemVariationStore;

    /// <summary>
    /// Shapes text with OpenType GSUB/GPOS using the provided font metrics.
    /// </summary>
    public static ShapingResult Shape(string text, FontMetrics metrics, string? languageTag = null)
        => Shape(text, metrics, new OpenTypeShapingOptions(LanguageTag: languageTag));

    /// <summary>
    /// Shapes text with OpenType GSUB/GPOS using a fallback chain of font metrics.
    /// </summary>
    public static ShapingResult Shape(string text, IReadOnlyList<FontMetrics> fallbackMetrics, string? languageTag = null)
        => Shape(text, fallbackMetrics, new OpenTypeShapingOptions(LanguageTag: languageTag));

    /// <summary>
    /// Shapes text with OpenType GSUB/GPOS using a fallback chain of font metrics.
    /// </summary>
    public static ShapingResult Shape(string text, IReadOnlyList<FontMetrics> fallbackMetrics, OpenTypeShapingOptions? options)
    {
        ArgumentNullException.ThrowIfNull(fallbackMetrics);
        if (fallbackMetrics.Count == 0)
            throw new ArgumentException("At least one font metrics entry is required for fallback shaping.", nameof(fallbackMetrics));

        if (fallbackMetrics.Count == 1)
            return Shape(text, fallbackMetrics[0], options);

        options ??= OpenTypeShapingOptions.Default;
        if (options.NormalizeInputToFormC && !text.IsNormalized(NormalizationForm.FormC))
            text = text.Normalize(NormalizationForm.FormC);

        var cmaps = new CmapTable?[fallbackMetrics.Count];
        for (int i = 0; i < fallbackMetrics.Count; i++)
            fallbackMetrics[i].Face.Tables.TryGet(out cmaps[i]);

        if (text.Length == 0)
            return new ShapingResult([], 0, TextShaperScript.Unknown, false);

        var runes = ParseRunes(text);
        var runs = ItemizeRuns(runes, options.RightToLeft);

        var combined = new List<ShapedGlyph>(Math.Max(text.Length, 8));
        double width = 0;

        for (int runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];
            int segmentStart = 0;
            int segmentLength = 0;
            int? currentFaceIndex = null;

            for (int i = 0; i < run.Length;)
            {
                int runeIndex = run.Start + i;
                int clusterStart = runes[runeIndex].Cluster;
                int clusterRuneLength = 1;
                while (i + clusterRuneLength < run.Length)
                {
                    int probeIndex = run.Start + i + clusterRuneLength;
                    if (runes[probeIndex].Cluster != clusterStart)
                        break;
                    clusterRuneLength++;
                }

                int faceIndex = ResolveFallbackFaceIndexForCluster(runes, runeIndex, clusterRuneLength, cmaps);

                if (currentFaceIndex is null)
                {
                    currentFaceIndex = faceIndex;
                    segmentStart = runeIndex;
                    segmentLength = clusterRuneLength;
                    i += clusterRuneLength;
                    continue;
                }

                if (faceIndex == currentFaceIndex.Value)
                {
                    segmentLength += clusterRuneLength;
                    i += clusterRuneLength;
                    continue;
                }

                ShapeFallbackSegment(text, segmentStart, segmentLength, fallbackMetrics[currentFaceIndex.Value], options, combined, ref width);

                currentFaceIndex = faceIndex;
                segmentStart = runeIndex;
                segmentLength = clusterRuneLength;
                i += clusterRuneLength;
            }

            if (currentFaceIndex is not null && segmentLength > 0)
                ShapeFallbackSegment(text, segmentStart, segmentLength, fallbackMetrics[currentFaceIndex.Value], options, combined, ref width);
        }

        bool singleRun = runs.Count == 1;
        var resultScript = singleRun ? runs[0].Script : TextShaperScript.Unknown;
        bool rtl = (singleRun && ((runs[0].BidiLevel & 1) == 1)) || options.RightToLeft == true;

        return new ShapingResult(combined, width, resultScript, rtl);
    }

    private static int ResolveFallbackFaceIndexForCluster(List<RuneInfo> runes, int clusterStartRuneIndex, int clusterRuneLength, CmapTable?[] cmaps)
    {
        for (int face = 0; face < cmaps.Length; face++)
        {
            if (cmaps[face] is not CmapTable cmap)
                continue;

            bool supportsCluster = true;
            for (int i = 0; i < clusterRuneLength; i++)
            {
                uint codePoint = (uint)runes[clusterStartRuneIndex + i].Rune.Value;
                if (!cmap.TryGetGlyphId(codePoint, out var glyphId) || glyphId == 0)
                {
                    supportsCluster = false;
                    break;
                }
            }

            if (supportsCluster)
                return face;
        }

        uint firstCodePoint = (uint)runes[clusterStartRuneIndex].Rune.Value;
        return ResolveFallbackFaceIndex(firstCodePoint, cmaps);
    }

    /// <summary>
    /// Shapes text with OpenType GSUB/GPOS using the provided font metrics.
    /// </summary>
    public static ShapingResult Shape(string text, FontMetrics metrics, OpenTypeShapingOptions? options)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= OpenTypeShapingOptions.Default;

        if (options.NormalizeInputToFormC && !text.IsNormalized(NormalizationForm.FormC))
            text = text.Normalize(NormalizationForm.FormC);

        // Primary shaping path uses HarfBuzz; legacy OpenType pipeline below is retained as compatibility fallback.
        if (TryShapeWithHarfBuzz(text, metrics, options, out var harfBuzzResult))
            return harfBuzzResult;

        int recursionLimit = options.MaxContextLookupRecursion is > 0 ? options.MaxContextLookupRecursion.Value : DefaultMaxContextLookupRecursion;

        var face = metrics.Face;
        if (!face.Tables.TryGet(out CmapTable cmap))
            throw new InvalidOperationException("The font does not contain a cmap table.");

        face.Tables.TryGet(out HmtxTable? hmtx);
        face.Tables.TryGet(out GsubTable? gsub);
        face.Tables.TryGet(out GposTable? gpos);
        face.Tables.TryGet(out GdefTable? gdef);
        face.Tables.TryGet(out KernTable? kern);
        face.Tables.TryGet(out FvarTable? fvar);
        face.Tables.TryGet(out AvarTable? avar);
        face.Tables.TryGet(out HvarTable? hvar);

        if (text.Length == 0)
            return new ShapingResult([], 0, TextShaperScript.Unknown, false);

        var runes = ParseRunes(text);
        var runs = ItemizeRuns(runes, options.RightToLeft);
        VariationInstance? variationInstance = BuildVariationInstance(fvar, avar, options.VariationCoordinates);
        OpenTypeShapingTableBlobs tableBlobs = OpenTypeShapingTableBlobs.FromFace(face);

        var shaped = new List<ShapedGlyph>(Math.Max(text.Length, 8));
        double width = 0;

        for (int runIndex = 0; runIndex < runs.Count; runIndex++)
        {
            var run = runs[runIndex];
            var scriptTags = GetScriptTags(run.Script);
            var scriptTag = scriptTags[0];

            var buffer = new List<GlyphInfo>(run.Length);
            for (int i = 0; i < run.Length; i++)
            {
                var runeInfo = runes[run.Start + i];
                ushort glyphId = cmap.TryGetGlyphId((uint)runeInfo.Rune.Value, out var gid) ? gid : (ushort)0;
                buffer.Add(new GlyphInfo(glyphId, (uint)runeInfo.Rune.Value, runeInfo.Cluster));
            }

            PreprocessIndicBuffer(run.Script, buffer, cmap);

            bool runRightToLeft = options.RightToLeft ?? ((run.BidiLevel & 1) == 1);
            var enabledFeatures = BuildEnabledFeatures(run.Script, options);

            var adjustments = new GlyphAdjustment[buffer.Count];

            ApplyScriptSpecificShapingEngine(
                face.Key,
                run.Script,
                options,
                tableBlobs,
                scriptTags,
                enabledFeatures,
                gsub,
                gpos,
                gdef,
                variationInstance,
                buffer,
                adjustments,
                recursionLimit);

            if (options.EnableLegacyKernFallback)
                ApplyLegacyKernFallback(face.Key, scriptTags, options.LanguageTag, enabledFeatures, kern, gpos, buffer, adjustments);

            if (runRightToLeft)
            {
                buffer.Reverse();
                Array.Reverse(adjustments);
            }

            RemoveJoinControlGlyphs(buffer, ref adjustments);

            for (int i = 0; i < buffer.Count; i++)
            {
                ushort glyphId = buffer[i].GlyphId;
                double baseAdvance = hmtx is null ? 0 : ResolveGlyphAdvanceWidth(hmtx, hvar, variationInstance, glyphId) * metrics.Scale;
                double xAdvance = baseAdvance + (adjustments[i].XAdvance * metrics.Scale);

                shaped.Add(new ShapedGlyph(
                    Face: metrics.Face,
                    GlyphId: glyphId,
                    CodePoint: buffer[i].CodePoint,
                    Cluster: buffer[i].Cluster,
                    XAdvance: xAdvance,
                    YAdvance: adjustments[i].YAdvance * metrics.Scale,
                    XOffset: adjustments[i].XOffset * metrics.Scale,
                    YOffset: adjustments[i].YOffset * metrics.Scale));

                width += xAdvance;
            }
        }

        bool singleRun = runs.Count == 1;
        var resultScript = singleRun ? runs[0].Script : TextShaperScript.Unknown;
        bool rtl = (singleRun && ((runs[0].BidiLevel & 1) == 1)) || options.RightToLeft == true;

        return new ShapingResult(shaped, width, resultScript, rtl);
    }

    private static int ResolveFallbackFaceIndex(uint codePoint, CmapTable?[] cmaps)
    {
        for (int i = 0; i < cmaps.Length; i++)
        {
            if (cmaps[i] is CmapTable cmap && cmap.TryGetGlyphId(codePoint, out var glyphId) && glyphId != 0)
                return i;
        }

        return 0;
    }

    private static void ShapeFallbackSegment(
        string sourceText,
        int segmentStartRune,
        int segmentRuneLength,
        FontMetrics segmentMetrics,
        OpenTypeShapingOptions options,
        List<ShapedGlyph> combined,
        ref double width)
    {
        if (segmentRuneLength <= 0)
            return;

        GetUtf16RangeFromRuneRange(sourceText, segmentStartRune, segmentRuneLength, out int utf16Start, out int utf16End);
        int length = utf16End - utf16Start;

        if (length <= 0)
            return;

        string segmentText = sourceText.Substring(utf16Start, length);
        var result = Shape(segmentText, segmentMetrics, options);

        for (int i = 0; i < result.Glyphs.Count; i++)
        {
            var glyph = result.Glyphs[i];
            combined.Add(glyph with { Cluster = utf16Start + glyph.Cluster });
        }

        width += result.Width;
    }

    private static void GetUtf16RangeFromRuneRange(string text, int runeStart, int runeLength, out int utf16Start, out int utf16End)
    {
        utf16Start = 0;
        utf16End = text.Length;

        int runeIndex = 0;
        for (int i = 0; i < text.Length;)
        {
            if (runeIndex == runeStart)
                utf16Start = i;
            if (runeIndex == runeStart + runeLength)
            {
                utf16End = i;
                return;
            }

            Rune.DecodeFromUtf16(text.AsSpan(i), out _, out int consumed);
            i += consumed;
            runeIndex++;
        }

        if (runeStart + runeLength >= runeIndex)
            utf16End = text.Length;
    }

    private static void ApplyScriptSpecificShapingEngine(
        string fontKey,
        TextShaperScript script,
        OpenTypeShapingOptions options,
        OpenTypeShapingTableBlobs tableBlobs,
        IReadOnlyList<string> scriptTags,
        IReadOnlyList<string> enabledFeatures,
        GsubTable? gsub,
        GposTable? gpos,
        GdefTable? gdef,
        VariationInstance? variationInstance,
        List<GlyphInfo> buffer,
        GlyphAdjustment[] adjustments,
        int recursionLimit)
    {
        string scriptTag = scriptTags[0];

        switch (script)
        {
            case TextShaperScript.Arabic:
            case TextShaperScript.Syriac:
                ApplyArabicShapingEngine(fontKey, script, options, tableBlobs, scriptTags, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
                break;

            case TextShaperScript.Devanagari:
            case TextShaperScript.Bengali:
            case TextShaperScript.Gurmukhi:
            case TextShaperScript.Gujarati:
            case TextShaperScript.Oriya:
            case TextShaperScript.Tamil:
            case TextShaperScript.Telugu:
            case TextShaperScript.Kannada:
            case TextShaperScript.Malayalam:
            case TextShaperScript.Sinhala:
            case TextShaperScript.Khmer:
            case TextShaperScript.Thai:
            case TextShaperScript.Lao:
            case TextShaperScript.Myanmar:
                ApplyIndicShapingEngine(fontKey, script, options, tableBlobs, scriptTags, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
                break;

            case TextShaperScript.Tibetan:
                ApplyTibetanShapingEngine(fontKey, script, options, tableBlobs, scriptTags, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
                break;

            case TextShaperScript.Hangul:
                ApplyHangulShapingEngine(fontKey, script, options, tableBlobs, scriptTags, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
                break;

            default:
                ApplyDefaultShapingEngine(fontKey, script, tableBlobs, scriptTags, options.LanguageTag, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
                break;
        }
    }

    private static void ApplyArabicShapingEngine(
        string fontKey,
        TextShaperScript script,
        OpenTypeShapingOptions options,
        OpenTypeShapingTableBlobs tableBlobs,
        IReadOnlyList<string> scriptTags,
        IReadOnlyList<string> enabledFeatures,
        GsubTable? gsub,
        GposTable? gpos,
        GdefTable? gdef,
        VariationInstance? variationInstance,
        List<GlyphInfo> buffer,
        GlyphAdjustment[] adjustments,
        int recursionLimit)
    {
        ApplyDefaultShapingEngine(fontKey, script, tableBlobs, scriptTags, options.LanguageTag, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
    }

    private static void ApplyTibetanShapingEngine(
        string fontKey,
        TextShaperScript script,
        OpenTypeShapingOptions options,
        OpenTypeShapingTableBlobs tableBlobs,
        IReadOnlyList<string> scriptTags,
        IReadOnlyList<string> enabledFeatures,
        GsubTable? gsub,
        GposTable? gpos,
        GdefTable? gdef,
        VariationInstance? variationInstance,
        List<GlyphInfo> buffer,
        GlyphAdjustment[] adjustments,
        int recursionLimit)
    {
        ApplyDefaultShapingEngine(fontKey, script, tableBlobs, scriptTags, options.LanguageTag, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
    }

    private static void ApplyHangulShapingEngine(
        string fontKey,
        TextShaperScript script,
        OpenTypeShapingOptions options,
        OpenTypeShapingTableBlobs tableBlobs,
        IReadOnlyList<string> scriptTags,
        IReadOnlyList<string> enabledFeatures,
        GsubTable? gsub,
        GposTable? gpos,
        GdefTable? gdef,
        VariationInstance? variationInstance,
        List<GlyphInfo> buffer,
        GlyphAdjustment[] adjustments,
        int recursionLimit)
    {
        ApplyDefaultShapingEngine(fontKey, script, tableBlobs, scriptTags, options.LanguageTag, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
    }

    private static void ApplyIndicShapingEngine(
        string fontKey,
        TextShaperScript script,
        OpenTypeShapingOptions options,
        OpenTypeShapingTableBlobs tableBlobs,
        IReadOnlyList<string> scriptTags,
        IReadOnlyList<string> enabledFeatures,
        GsubTable? gsub,
        GposTable? gpos,
        GdefTable? gdef,
        VariationInstance? variationInstance,
        List<GlyphInfo> buffer,
        GlyphAdjustment[] adjustments,
        int recursionLimit)
    {
        string scriptTag = scriptTags[0];

        if (options.ApplyIndicPreReordering)
            ReorderIndicGlyphs(script, buffer);

        ApplyDefaultShapingEngine(fontKey, script, tableBlobs, scriptTags, options.LanguageTag, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
    }

    private static void PreprocessIndicBuffer(TextShaperScript script, List<GlyphInfo> buffer, CmapTable cmap)
    {
        if (!IsIndicScript(script) || buffer.Count == 0)
            return;

        ExpandIndicSplitMatras(script, buffer, cmap);
        InsertDottedCircleForBrokenIndicSyllables(script, buffer, cmap);
    }

    private static void ApplyDefaultShapingEngine(
        string fontKey,
        TextShaperScript script,
        OpenTypeShapingTableBlobs tableBlobs,
        IReadOnlyList<string> scriptTags,
        string? languageTag,
        IReadOnlyList<string> enabledFeatures,
        GsubTable? gsub,
        GposTable? gpos,
        GdefTable? gdef,
        VariationInstance? variationInstance,
        List<GlyphInfo> buffer,
        GlyphAdjustment[] adjustments,
        int recursionLimit)
    {
        string scriptTag = scriptTags[0];

        if (gsub is not null)
            ApplyGsubByFeatureStages(fontKey, gsub, scriptTags, languageTag, script, enabledFeatures, buffer, recursionLimit);

        if (gpos is null || buffer.Count == 0)
            return;

        var gposLookupIndices = GetCachedLookupIndices(fontKey, "GPOS", gpos.ScriptList, gpos.FeatureList, gpos.LookupList.Length, scriptTags, languageTag, enabledFeatures);
        ApplyGpos(gpos, gdef, variationInstance, gposLookupIndices, buffer, adjustments, recursionLimit);
    }

    private static void ApplyLegacyKernFallback(
        string fontKey,
        IReadOnlyList<string> scriptTags,
        string? languageTag,
        IReadOnlyList<string> enabledFeatures,
        KernTable? kern,
        GposTable? gpos,
        List<GlyphInfo> buffer,
        GlyphAdjustment[] adjustments)
    {
        if (kern is null || buffer.Count < 2)
            return;
        if (!ContainsFeatureTag(enabledFeatures, "kern"))
            return;
        if (HasGposKern(gpos, fontKey, scriptTags, languageTag, enabledFeatures))
            return;

        ApplyLegacyKernPairAdjustments(kern, buffer, adjustments);
    }

    private static bool HasGposKern(GposTable? gpos, string fontKey, IReadOnlyList<string> scriptTags, string? languageTag, IReadOnlyList<string> enabledFeatures)
    {
        if (gpos is null)
            return false;

        var lookupIndices = GetCachedLookupIndices(fontKey, "GPOS", gpos.ScriptList, gpos.FeatureList, gpos.LookupList.Length, scriptTags, languageTag, ["kern"]);
        for (int i = 0; i < lookupIndices.Length; i++)
        {
            ushort lookupIndex = lookupIndices[i];
            if (lookupIndex >= gpos.LookupList.Length)
                continue;

            if (gpos.LookupList[lookupIndex].LookupType == (ushort)GposTable.LookupType.PairAdjustment)
                return true;
        }

        return false;
    }

    private static bool ContainsFeatureTag(IReadOnlyList<string> enabledFeatures, string tag)
    {
        for (int i = 0; i < enabledFeatures.Count; i++)
        {
            if (string.Equals(enabledFeatures[i], tag, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void ApplyLegacyKernPairAdjustments(KernTable kern, List<GlyphInfo> buffer, GlyphAdjustment[] adjustments)
    {
        for (int i = 0; i < buffer.Count - 1; i++)
        {
            int kernAdjust = kern.GetKerningAdjustment(buffer[i].GlyphId, buffer[i + 1].GlyphId);
            if (kernAdjust == 0)
                continue;

            adjustments[i].XAdvance += kernAdjust;
        }
    }

    private static void RemoveJoinControlGlyphs(List<GlyphInfo> buffer, ref GlyphAdjustment[] adjustments)
    {
        if (buffer.Count == 0)
            return;

        int write = 0;
        for (int read = 0; read < buffer.Count; read++)
        {
            if (IsJoinControl(buffer[read].CodePoint))
                continue;

            if (write != read)
            {
                buffer[write] = buffer[read];
                adjustments[write] = adjustments[read];
            }

            write++;
        }

        if (write == buffer.Count)
            return;

        if (write == 0)
        {
            buffer.Clear();
            adjustments = [];
            return;
        }

        buffer.RemoveRange(write, buffer.Count - write);
        Array.Resize(ref adjustments, write);
    }

    private static List<string> BuildEnabledFeatures(TextShaperScript script, OpenTypeShapingOptions options)
    {
        var features = new List<string>(GetDefaultFeatures(script));

        if (options.ExtraFeatures is not null)
        {
            foreach (var featureTag in options.ExtraFeatures)
            {
                if (TryNormalizeFeatureTag(featureTag, out var normalized))
                {
                    if (!ContainsFeature(features, normalized))
                        features.Add(normalized);
                }
            }
        }

        if (options.DisabledFeatures is not null)
        {
            foreach (var featureTag in options.DisabledFeatures)
            {
                if (TryNormalizeFeatureTag(featureTag, out var normalized))
                    RemoveFeature(features, normalized);
            }
        }

        return features;
    }

    private static bool ContainsFeature(List<string> features, string featureTag)
    {
        for (int i = 0; i < features.Count; i++)
            if (string.Equals(features[i], featureTag, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static void RemoveFeature(List<string> features, string featureTag)
    {
        for (int i = features.Count - 1; i >= 0; i--)
        {
            if (string.Equals(features[i], featureTag, StringComparison.Ordinal))
                features.RemoveAt(i);
        }
    }

    private static bool TryNormalizeFeatureTag(string? featureTag, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(featureTag))
            return false;

        featureTag = featureTag.Trim();
        if (featureTag.Length != 4)
            return false;

        normalized = featureTag.ToLowerInvariant();
        return true;
    }

    private static string[] GetDefaultFeatures(TextShaperScript script) => script switch
    {
        TextShaperScript.Arabic => ["ccmp", "locl", "isol", "fina", "fin2", "fin3", "medi", "med2", "init", "rlig", "liga", "calt", "kern", "mark", "mkmk"],
        TextShaperScript.Syriac => ["ccmp", "locl", "isol", "fina", "fin2", "fin3", "medi", "med2", "init", "rlig", "liga", "calt", "kern", "mark", "mkmk"],
        TextShaperScript.Devanagari => ["ccmp", "locl", "nukt", "akhn", "rphf", "rkrf", "pref", "blwf", "abvf", "half", "pstf", "vatu", "cjct", "kern", "mark", "mkmk", "liga"],
        TextShaperScript.Bengali or
        TextShaperScript.Gurmukhi or
        TextShaperScript.Gujarati or
        TextShaperScript.Oriya or
        TextShaperScript.Tamil or
        TextShaperScript.Telugu or
        TextShaperScript.Kannada or
        TextShaperScript.Malayalam or
        TextShaperScript.Sinhala or
        TextShaperScript.Khmer or
        TextShaperScript.Thai or
        TextShaperScript.Lao or
        TextShaperScript.Myanmar => ["ccmp", "locl", "nukt", "akhn", "rphf", "rkrf", "pref", "blwf", "abvf", "half", "pstf", "vatu", "cjct", "kern", "mark", "mkmk", "liga"],
        TextShaperScript.Tibetan => ["ccmp", "locl", "abvs", "blws", "kern", "mark", "mkmk"],
        TextShaperScript.Hangul => ["ccmp", "locl", "ljmo", "vjmo", "tjmo", "kern", "mark", "mkmk"],
        _ => ["ccmp", "locl", "liga", "clig", "calt", "kern", "mark", "mkmk"]
    };

    internal static bool TryFindPreviousBaseGlyph(List<GlyphInfo> buffer, int startIndex, Coverage coverage, GdefTable? gdef, out int index, out ushort coverageIndex)
    {
        for (int i = startIndex - 1; i >= 0; i--)
        {
            if (IsMarkGlyph(buffer[i].GlyphId, gdef))
                continue;
            if (!coverage.TryGetIndex(buffer[i].GlyphId, out coverageIndex))
                continue;

            index = i;
            return true;
        }

        index = -1;
        coverageIndex = 0;
        return false;
    }

    internal static bool TryFindPreviousMarkGlyph(List<GlyphInfo> buffer, int startIndex, Coverage coverage, GdefTable? gdef, out int index, out ushort coverageIndex)
    {
        for (int i = startIndex - 1; i >= 0; i--)
        {
            if (!IsMarkGlyph(buffer[i].GlyphId, gdef))
                continue;
            if (!coverage.TryGetIndex(buffer[i].GlyphId, out coverageIndex))
                continue;

            index = i;
            return true;
        }

        index = -1;
        coverageIndex = 0;
        return false;
    }

    internal static bool TryFindPreviousLigatureGlyph(List<GlyphInfo> buffer, int startIndex, Coverage coverage, GdefTable? gdef, out int index, out ushort coverageIndex)
    {
        for (int i = startIndex - 1; i >= 0; i--)
        {
            if (gdef is not null && !IsLigatureGlyph(buffer[i].GlyphId, gdef))
                continue;
            if (!coverage.TryGetIndex(buffer[i].GlyphId, out coverageIndex))
                continue;

            index = i;
            return true;
        }

        index = -1;
        coverageIndex = 0;
        return false;
    }

    private static bool IsMarkGlyph(ushort glyphId, GdefTable? gdef)
    {
        if (gdef?.GlyphClassDef is not ClassDef classDef)
            return false;

        return classDef.TryGetClass(glyphId, out ushort glyphClass) && glyphClass == (ushort)GdefTable.GlyphClass.MarkGlyph;
    }

    private static bool IsLigatureGlyph(ushort glyphId, GdefTable? gdef)
    {
        if (gdef?.GlyphClassDef is not ClassDef classDef)
            return false;

        return classDef.TryGetClass(glyphId, out ushort glyphClass) && glyphClass == (ushort)GdefTable.GlyphClass.LigatureGlyph;
    }

    private static bool IsBaseGlyph(ushort glyphId, GdefTable? gdef)
    {
        if (gdef?.GlyphClassDef is not ClassDef classDef)
            return false;

        return classDef.TryGetClass(glyphId, out ushort glyphClass) && glyphClass == (ushort)GdefTable.GlyphClass.BaseGlyph;
    }

    internal static bool TryGetAnchorCoordinates(GposTable.AnchorTable? anchor, out short x, out short y)
    {
        switch (anchor)
        {
            case GposTable.AnchorFormat1 a1:
                x = a1.XCoordinate;
                y = a1.YCoordinate;
                return true;
            case GposTable.AnchorFormat2 a2:
                x = a2.XCoordinate;
                y = a2.YCoordinate;
                return true;
            case GposTable.AnchorFormat3 a3:
                x = a3.XCoordinate;
                y = a3.YCoordinate;
                return true;
            default:
                x = 0;
                y = 0;
                return false;
        }
    }

    internal static ushort[] ToGlyphIds(List<GlyphInfo> buffer)
    {
        var glyphIds = new ushort[buffer.Count];
        for (int i = 0; i < buffer.Count; i++)
            glyphIds[i] = buffer[i].GlyphId;
        return glyphIds;
    }

    private static double ResolveValueDelta(DeviceOrVariationIndexTable? deviceOrVariation)
    {
        if (deviceOrVariation is not VariationIndexTable variationIndex)
            return 0;
        if (currentItemVariationStore is not ItemVariationStore itemVariationStore || currentVariationInstance is not VariationInstance variationInstance)
            return 0;

        return itemVariationStore.Resolve(variationIndex, variationInstance);
    }

    private static float ResolveGlyphAdvanceWidth(HmtxTable hmtx, HvarTable? hvar, VariationInstance? variationInstance, ushort glyphId)
    {
        float advance = hmtx.GetAdvanceWidth(glyphId);

        if (hvar is null || variationInstance is null)
            return advance;

        var variationIndex = ResolveAdvanceVariationIndex(hvar, glyphId);
        float delta = hvar.ItemVariationStore.Resolve(variationIndex, variationInstance.Value);
        return advance + delta;
    }

    private static VariationIndexTable ResolveAdvanceVariationIndex(HvarTable hvar, ushort glyphId)
    {
        if (hvar.AdvanceWidthMapping is not DeltaSetIndexMap advanceMap)
            return new VariationIndexTable(DeviceDeltaFormat.VariationIndex, glyphId, 0);

        var (outer, inner) = advanceMap.Map(glyphId);
        return new VariationIndexTable(DeviceDeltaFormat.VariationIndex, outer, inner);
    }

    private static VariationInstance? BuildVariationInstance(FvarTable? fvar, AvarTable? avar, IReadOnlyDictionary<string, float>? coordinates)
    {
        if (fvar is null || fvar.Axes.Length == 0)
            return null;

        var normalizedCoordinates = new float[fvar.Axes.Length];

        for (int i = 0; i < fvar.Axes.Length; i++)
        {
            var axis = fvar.Axes[i];
            float designValue = axis.DefaultValue;

            if (coordinates is not null && coordinates.TryGetValue(axis.AxisTag, out float requested))
                designValue = requested;

            designValue = Math.Clamp(designValue, axis.MinValue, axis.MaxValue);
            float normalized = NormalizeAxisCoordinate(axis, designValue);

            if (avar is not null && i < avar.SegmentMaps.Length)
                normalized = ApplyAvarMapping(avar.SegmentMaps[i], normalized);

            normalizedCoordinates[i] = normalized;
        }

        return new VariationInstance(normalizedCoordinates);
    }

    private static float NormalizeAxisCoordinate(FvarTable.AxisRecord axis, float designValue)
    {
        if (designValue == axis.DefaultValue)
            return 0f;

        if (designValue < axis.DefaultValue)
        {
            float denom = axis.DefaultValue - axis.MinValue;
            return denom <= 0f ? 0f : (designValue - axis.DefaultValue) / denom;
        }

        float maxDenom = axis.MaxValue - axis.DefaultValue;
        return maxDenom <= 0f ? 0f : (designValue - axis.DefaultValue) / maxDenom;
    }

    private static float ApplyAvarMapping(AvarTable.SegmentMap segmentMap, float normalized)
    {
        var maps = segmentMap.AxisValueMaps;
        if (maps.Length == 0)
            return normalized;

        if (normalized <= maps[0].FromCoordinate)
            return maps[0].ToCoordinate;

        for (int i = 1; i < maps.Length; i++)
        {
            var prev = maps[i - 1];
            var next = maps[i];

            if (normalized > next.FromCoordinate)
                continue;

            float range = next.FromCoordinate - prev.FromCoordinate;
            if (range == 0)
                return next.ToCoordinate;

            float t = (normalized - prev.FromCoordinate) / range;
            return prev.ToCoordinate + ((next.ToCoordinate - prev.ToCoordinate) * t);
        }

        return maps[^1].ToCoordinate;
    }

    private static string[] GetScriptTags(TextShaperScript script) => script switch
    {
        TextShaperScript.Latin => ["latn", "DFLT"],
        TextShaperScript.Arabic => ["arab", "DFLT"],
        TextShaperScript.Devanagari => ["deva", "dev2", "DFLT"],
        TextShaperScript.Bengali => ["bng2", "beng", "DFLT"],
        TextShaperScript.Gurmukhi => ["gur2", "guru", "DFLT"],
        TextShaperScript.Gujarati => ["gjr2", "gujr", "DFLT"],
        TextShaperScript.Oriya => ["ory2", "orya", "DFLT"],
        TextShaperScript.Tamil => ["tml2", "taml", "DFLT"],
        TextShaperScript.Telugu => ["tel2", "telu", "DFLT"],
        TextShaperScript.Kannada => ["knd2", "knda", "DFLT"],
        TextShaperScript.Malayalam => ["mlm2", "mlym", "DFLT"],
        TextShaperScript.Sinhala => ["sinh", "DFLT"],
        TextShaperScript.Khmer => ["khmr", "DFLT"],
        TextShaperScript.Thai => ["thai", "DFLT"],
        TextShaperScript.Lao => ["lao ", "DFLT"],
        TextShaperScript.Myanmar => ["mym2", "mymr", "DFLT"],
        TextShaperScript.Hebrew => ["hebr", "DFLT"],
        TextShaperScript.Syriac => ["syrc", "DFLT"],
        TextShaperScript.Tibetan => ["tibt", "DFLT"],
        TextShaperScript.Hangul => ["hang", "DFLT"],
        _ => ["DFLT"]
    };

    private static string ToScriptTag(TextShaperScript script) => GetScriptTags(script)[0];

    private static bool IsRightToLeftScript(TextShaperScript script)
        => script is TextShaperScript.Arabic or TextShaperScript.Hebrew;

    private static bool IsJoinControl(uint codePoint)
        => codePoint is 0x200C or 0x200D;

    internal readonly record struct RuneInfo(Rune Rune, int Cluster, TextShaperScript Script, bool RightToLeft);

    internal readonly record struct TextRun(int Start, int Length, TextShaperScript Script, byte BidiLevel);

    internal readonly record struct GlyphInfo(ushort GlyphId, uint CodePoint, int Cluster);

    internal readonly record struct LookupPlanCacheKey(string FontKey, string TableTag, string ScriptTag, string LanguageTag, string FeaturesKey);

    internal readonly record struct LookupFilterContext(LookupFlag LookupFlag, ushort MarkFilteringSet, GdefTable? Gdef);

    internal struct GlyphAdjustment
    {
        public double XAdvance, YAdvance, XOffset, YOffset;

        public bool ApplyValue(GposTable.ValueRecord value)
        {
            double xPlacement = value.XPlacement + ResolveValueDelta(value.XPlaDevice);
            double yPlacement = value.YPlacement + ResolveValueDelta(value.YPlaDevice);
            double xAdvance = value.XAdvance + ResolveValueDelta(value.XAdvDevice);
            double yAdvance = value.YAdvance + ResolveValueDelta(value.YAdvDevice);

            bool changed = xPlacement != 0 || yPlacement != 0 || xAdvance != 0 || yAdvance != 0;
            XOffset += xPlacement;
            YOffset += yPlacement;
            XAdvance += xAdvance;
            YAdvance += yAdvance;
            return changed;
        }
    }
}
