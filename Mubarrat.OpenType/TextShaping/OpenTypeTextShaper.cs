using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.Tables;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Mubarrat.OpenType.TextShaping;

public static class OpenTypeTextShaper
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

            bool runRightToLeft = options.RightToLeft ?? ((run.BidiLevel & 1) == 1);
            var enabledFeatures = BuildEnabledFeatures(run.Script, options);

            var adjustments = new GlyphAdjustment[buffer.Count];

            ApplyScriptSpecificShapingEngine(
                face.Key,
                run.Script,
                options,
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
                ApplyArabicShapingEngine(fontKey, script, options, scriptTags, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
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
                ApplyIndicShapingEngine(fontKey, script, options, scriptTags, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
                break;

            case TextShaperScript.Tibetan:
                ApplyTibetanShapingEngine(fontKey, script, options, scriptTags, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
                break;

            case TextShaperScript.Hangul:
                ApplyHangulShapingEngine(fontKey, script, options, scriptTags, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
                break;

            default:
                ApplyDefaultShapingEngine(fontKey, script, scriptTags, options.LanguageTag, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
                break;
        }
    }

    private static void ApplyArabicShapingEngine(
        string fontKey,
        TextShaperScript script,
        OpenTypeShapingOptions options,
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
        ApplyDefaultShapingEngine(fontKey, script, scriptTags, options.LanguageTag, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
    }

    private static void ApplyTibetanShapingEngine(
        string fontKey,
        TextShaperScript script,
        OpenTypeShapingOptions options,
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
        ApplyDefaultShapingEngine(fontKey, script, scriptTags, options.LanguageTag, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
    }

    private static void ApplyHangulShapingEngine(
        string fontKey,
        TextShaperScript script,
        OpenTypeShapingOptions options,
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
        ApplyDefaultShapingEngine(fontKey, script, scriptTags, options.LanguageTag, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
    }

    private static void ApplyIndicShapingEngine(
        string fontKey,
        TextShaperScript script,
        OpenTypeShapingOptions options,
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

        ApplyDefaultShapingEngine(fontKey, script, scriptTags, options.LanguageTag, enabledFeatures, gsub, gpos, gdef, variationInstance, buffer, adjustments, recursionLimit);
    }

    private static void ApplyDefaultShapingEngine(
        string fontKey,
        TextShaperScript script,
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

    private static List<RuneInfo> ParseRunes(string text)
    {
        var runes = new List<RuneInfo>(text.Length);
        var clusterMap = BuildGraphemeClusterMap(text);

        for (int i = 0; i < text.Length;)
        {
            Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out int consumed);

            var script = ClassifyScript((uint)rune.Value);
            bool rtl = IsRightToLeftScript(script);

            int cluster = clusterMap[i];
            runes.Add(new RuneInfo(rune, cluster, script, rtl));
            i += consumed;
        }

        for (int i = 0; i < runes.Count; i++)
        {
            if (runes[i].Script != TextShaperScript.Unknown)
                continue;

            if (i > 0)
                runes[i] = runes[i] with { Script = runes[i - 1].Script, RightToLeft = runes[i - 1].RightToLeft };
            else
                runes[i] = runes[i] with { Script = TextShaperScript.Latin, RightToLeft = false };
        }

        return runes;
    }

    private static int[] BuildGraphemeClusterMap(string text)
    {
        var clusterMap = new int[text.Length];
        if (text.Length == 0)
            return clusterMap;

        var runeStarts = new List<int>(text.Length);
        var runeClasses = new List<GraphemeBreakClass>(text.Length);
        var runeValues = new List<uint>(text.Length);

        for (int i = 0; i < text.Length;)
        {
            runeStarts.Add(i);
            Rune.DecodeFromUtf16(text.AsSpan(i), out var rune, out int consumed);
            runeValues.Add((uint)rune.Value);
            runeClasses.Add(ClassifyGraphemeBreak(rune));
            i += consumed;
        }

        int currentClusterStart = runeStarts[0];
        int regionalIndicatorCount = runeClasses[0] == GraphemeBreakClass.RegionalIndicator ? 1 : 0;

        for (int r = 0; r < runeStarts.Count; r++)
        {
            int start = runeStarts[r];
            int end = (r + 1 < runeStarts.Count) ? runeStarts[r + 1] : text.Length;
            for (int i = start; i < end; i++)
                clusterMap[i] = currentClusterStart;

            if (r + 1 >= runeStarts.Count)
                break;

            bool shouldBreak = ShouldBreakGrapheme(
                runeClasses,
                runeValues,
                r,
                regionalIndicatorCount);

            if (shouldBreak)
            {
                currentClusterStart = runeStarts[r + 1];
                regionalIndicatorCount = 0;
            }

            if (runeClasses[r + 1] == GraphemeBreakClass.RegionalIndicator)
                regionalIndicatorCount++;
            else
                regionalIndicatorCount = 0;
        }

        return clusterMap;
    }

    private static bool ShouldBreakGrapheme(List<GraphemeBreakClass> classes, List<uint> runeValues, int index, int regionalIndicatorCount)
    {
        GraphemeBreakClass current = classes[index];
        GraphemeBreakClass next = classes[index + 1];

        if (current == GraphemeBreakClass.CR && next == GraphemeBreakClass.LF)
            return false;

        if (current is GraphemeBreakClass.CR or GraphemeBreakClass.LF or GraphemeBreakClass.Control
            || next is GraphemeBreakClass.CR or GraphemeBreakClass.LF or GraphemeBreakClass.Control)
            return true;

        if (current == GraphemeBreakClass.L && next is GraphemeBreakClass.L or GraphemeBreakClass.V or GraphemeBreakClass.LV or GraphemeBreakClass.LVT)
            return false;
        if (current is GraphemeBreakClass.LV or GraphemeBreakClass.V && next is GraphemeBreakClass.V or GraphemeBreakClass.T)
            return false;
        if (current is GraphemeBreakClass.LVT or GraphemeBreakClass.T && next == GraphemeBreakClass.T)
            return false;

        if (next is GraphemeBreakClass.Extend or GraphemeBreakClass.ZWJ)
            return false;
        if (next == GraphemeBreakClass.SpacingMark)
            return false;
        if (current == GraphemeBreakClass.Prepend)
            return false;

        if (current == GraphemeBreakClass.ZWJ)
        {
            int left = index - 1;
            while (left >= 0 && classes[left] == GraphemeBreakClass.Extend)
                left--;

            if (left >= 0 && IsExtendedPictographic(runeValues[left]) && IsExtendedPictographic(runeValues[index + 1]))
                return false;
        }

        if (current == GraphemeBreakClass.RegionalIndicator && next == GraphemeBreakClass.RegionalIndicator)
            return (regionalIndicatorCount % 2) == 1;

        return true;
    }

    private static GraphemeBreakClass ClassifyGraphemeBreak(Rune rune)
    {
        uint cp = (uint)rune.Value;

        if (cp == 0x000D)
            return GraphemeBreakClass.CR;
        if (cp == 0x000A)
            return GraphemeBreakClass.LF;
        if (cp == 0x200D)
            return GraphemeBreakClass.ZWJ;

        if (cp is >= 0x1F1E6 and <= 0x1F1FF)
            return GraphemeBreakClass.RegionalIndicator;

        if (cp is >= 0x1100 and <= 0x115F || cp is >= 0xA960 and <= 0xA97C)
            return GraphemeBreakClass.L;
        if (cp is >= 0x1160 and <= 0x11A7 || cp is >= 0xD7B0 and <= 0xD7C6)
            return GraphemeBreakClass.V;
        if (cp is >= 0x11A8 and <= 0x11FF || cp is >= 0xD7CB and <= 0xD7FB)
            return GraphemeBreakClass.T;
        if (cp is >= 0xAC00 and <= 0xD7A3)
            return ((cp - 0xAC00) % 28) == 0 ? GraphemeBreakClass.LV : GraphemeBreakClass.LVT;

        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
            || (category == UnicodeCategory.Format && cp is not 0x200C and not 0x200D))
            return GraphemeBreakClass.Control;

        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark || IsEmojiModifier(cp))
            return GraphemeBreakClass.Extend;

        if (category == UnicodeCategory.SpacingCombiningMark || cp is 0x0E33 or 0x0EB3)
            return GraphemeBreakClass.SpacingMark;

        if (IsPrepend(cp))
            return GraphemeBreakClass.Prepend;

        return GraphemeBreakClass.Other;
    }

    private static bool IsPrepend(uint cp)
        => cp is >= 0x0600 and <= 0x0605
        or 0x06DD
        or 0x070F
        or 0x0890
        or 0x0891
        or 0x08E2
        or 0x110BD
        or >= 0x111C2 and <= 0x111C3
        or 0x11A3A
        or >= 0x11A86 and <= 0x11A89
        or >= 0x11D46 and <= 0x11D47;

    private static bool IsEmojiModifier(uint cp) => cp is >= 0x1F3FB and <= 0x1F3FF;

    private static bool IsExtendedPictographic(uint cp)
        => cp is >= 0x1F300 and <= 0x1FAFF
        || cp is >= 0x2600 and <= 0x27BF
        || cp is >= 0x2300 and <= 0x23FF;

    private static List<TextRun> ItemizeRuns(List<RuneInfo> runes, bool? forceRightToLeft)
    {
        var runs = new List<TextRun>();
        if (runes.Count == 0)
            return runs;

        int paragraphLevel = forceRightToLeft.HasValue
            ? forceRightToLeft.Value ? 1 : 0
            : DetermineParagraphLevel(runes);

        var levels = ResolveBidiLevels(runes, paragraphLevel);

        int start = 0;
        var script = runes[0].Script;
        byte level = levels[0];

        for (int i = 1; i < runes.Count; i++)
        {
            if (runes[i].Script == script && levels[i] == level)
                continue;

            runs.Add(new TextRun(start, i - start, script, level));
            start = i;
            script = runes[i].Script;
            level = levels[i];
        }

        runs.Add(new TextRun(start, runes.Count - start, script, level));
        ReorderRunsByBidiLevels(runs);

        return runs;
    }

    private static int DetermineParagraphLevel(List<RuneInfo> runes)
    {
        for (int i = 0; i < runes.Count; i++)
        {
            var bidi = ClassifyBidi(runes[i].Rune);
            if (bidi is BidiClass.RightToLeft or BidiClass.ArabicLetter)
                return 1;
            if (bidi is BidiClass.LeftToRight)
                return 0;
        }

        return 0;
    }

    private static byte[] ResolveBidiLevels(List<RuneInfo> runes, int paragraphLevel)
    {
        if (runes.Count == 0)
            return [];

        var classes = new BidiClass[runes.Count];
        for (int i = 0; i < runes.Count; i++)
            classes[i] = ClassifyBidi(runes[i].Rune);

        ResolveExplicitEmbeddingLevels(classes, paragraphLevel, out var levels, out var overrides);
        ResolveWeakTypes(classes, levels, overrides, paragraphLevel);
        ResolveNeutralTypes(classes, levels, paragraphLevel);
        ResolveImplicitLevels(classes, levels, paragraphLevel);
        ApplyParagraphAndBoundaryResets(classes, levels, paragraphLevel);

        return levels;
    }

    private static void ResolveExplicitEmbeddingLevels(BidiClass[] classes, int paragraphLevel, out byte[] levels, out BidiClass[] overrides)
    {
        const int maxDepth = 125;

        levels = new byte[classes.Length];
        overrides = new BidiClass[classes.Length];
        for (int i = 0; i < overrides.Length; i++)
            overrides[i] = BidiClass.Neutral;

        var stack = new List<(byte Level, BidiClass Override, bool Isolate)>(32)
        {
            ((byte)paragraphLevel, BidiClass.Neutral, false)
        };

        int overflowIsolateCount = 0;
        int overflowEmbeddingCount = 0;
        int validIsolateCount = 0;

        for (int i = 0; i < classes.Length; i++)
        {
            var entry = stack[^1];
            levels[i] = entry.Level;
            overrides[i] = entry.Override;

            switch (classes[i])
            {
                case BidiClass.RightToLeftEmbedding:
                case BidiClass.LeftToRightEmbedding:
                case BidiClass.RightToLeftOverride:
                case BidiClass.LeftToRightOverride:
                    {
                        byte nextLevel = ComputeNextLevel(entry.Level,
                            classes[i] is BidiClass.RightToLeftEmbedding or BidiClass.RightToLeftOverride);

                        if (nextLevel <= maxDepth && overflowIsolateCount == 0 && overflowEmbeddingCount == 0)
                        {
                            BidiClass overrideState = classes[i] switch
                            {
                                BidiClass.RightToLeftOverride => BidiClass.RightToLeft,
                                BidiClass.LeftToRightOverride => BidiClass.LeftToRight,
                                _ => BidiClass.Neutral
                            };

                            stack.Add((nextLevel, overrideState, false));
                        }
                        else if (overflowIsolateCount == 0)
                        {
                            overflowEmbeddingCount++;
                        }

                        break;
                    }

                case BidiClass.RightToLeftIsolate:
                case BidiClass.LeftToRightIsolate:
                case BidiClass.FirstStrongIsolate:
                    {
                        bool rtlIsolate = classes[i] == BidiClass.RightToLeftIsolate;
                        if (classes[i] == BidiClass.FirstStrongIsolate)
                            rtlIsolate = DetermineIsolateDirection(classes, i, paragraphLevel);

                        byte nextLevel = ComputeNextLevel(entry.Level, rtlIsolate);

                        if (nextLevel <= maxDepth && overflowIsolateCount == 0 && overflowEmbeddingCount == 0)
                        {
                            validIsolateCount++;
                            stack.Add((nextLevel, BidiClass.Neutral, true));
                        }
                        else
                        {
                            overflowIsolateCount++;
                        }

                        break;
                    }

                case BidiClass.PopDirectionalIsolate:
                    {
                        if (overflowIsolateCount > 0)
                        {
                            overflowIsolateCount--;
                        }
                        else if (validIsolateCount > 0)
                        {
                            overflowEmbeddingCount = 0;

                            while (stack.Count > 0 && !stack[^1].Isolate)
                                stack.RemoveAt(stack.Count - 1);

                            if (stack.Count > 1)
                            {
                                stack.RemoveAt(stack.Count - 1);
                                validIsolateCount--;
                            }
                        }

                        levels[i] = stack[^1].Level;
                        overrides[i] = stack[^1].Override;
                        break;
                    }

                case BidiClass.PopDirectionalFormat:
                    {
                        if (overflowIsolateCount > 0)
                        {
                            // ignored inside overflow isolate
                        }
                        else if (overflowEmbeddingCount > 0)
                        {
                            overflowEmbeddingCount--;
                        }
                        else if (stack.Count > 1 && !stack[^1].Isolate)
                        {
                            stack.RemoveAt(stack.Count - 1);
                        }

                        levels[i] = stack[^1].Level;
                        overrides[i] = stack[^1].Override;
                        break;
                    }

                default:
                    if (entry.Override is BidiClass.LeftToRight or BidiClass.RightToLeft)
                    {
                        classes[i] = entry.Override;
                        overrides[i] = entry.Override;
                    }
                    break;
            }
        }
    }

    private static byte ComputeNextLevel(byte currentLevel, bool rtl)
    {
        if (rtl)
            return (byte)((currentLevel + 1) | 1);

        int candidate = currentLevel + 1;
        return (byte)(candidate % 2 == 0 ? candidate : candidate + 1);
    }

    private static bool DetermineIsolateDirection(BidiClass[] classes, int isolateStart, int paragraphLevel)
    {
        int depth = 1;
        for (int i = isolateStart + 1; i < classes.Length; i++)
        {
            switch (classes[i])
            {
                case BidiClass.LeftToRightIsolate:
                case BidiClass.RightToLeftIsolate:
                case BidiClass.FirstStrongIsolate:
                    depth++;
                    continue;

                case BidiClass.PopDirectionalIsolate:
                    depth--;
                    if (depth == 0)
                        return paragraphLevel == 1;
                    continue;

                case BidiClass.RightToLeft:
                case BidiClass.ArabicLetter:
                    return true;

                case BidiClass.LeftToRight:
                    return false;
            }
        }

        return paragraphLevel == 1;
    }

    private static void ResolveWeakTypes(BidiClass[] classes, byte[] levels, BidiClass[] overrides, int paragraphLevel)
    {
        for (int i = 0; i < classes.Length; i++)
        {
            if (classes[i] != BidiClass.NonSpacingMark)
                continue;

            int previous = FindPreviousVisible(classes, i - 1);
            classes[i] = previous < 0
                ? (paragraphLevel == 1 ? BidiClass.RightToLeft : BidiClass.LeftToRight)
                : classes[previous] is BidiClass.LeftToRightIsolate or BidiClass.RightToLeftIsolate or BidiClass.FirstStrongIsolate or BidiClass.PopDirectionalIsolate
                    ? BidiClass.Neutral
                    : classes[previous];
        }

        for (int i = 0; i < classes.Length; i++)
        {
            if (classes[i] != BidiClass.EuropeanNumber)
                continue;

            int previousStrong = FindPreviousStrong(classes, i - 1);
            if (previousStrong >= 0 && classes[previousStrong] == BidiClass.ArabicLetter)
                classes[i] = BidiClass.ArabicNumber;
        }

        for (int i = 0; i < classes.Length; i++)
        {
            if (classes[i] == BidiClass.ArabicLetter)
                classes[i] = BidiClass.RightToLeft;
        }

        for (int i = 1; i < classes.Length - 1; i++)
        {
            if (classes[i] == BidiClass.EuropeanSeparator && classes[i - 1] == BidiClass.EuropeanNumber && classes[i + 1] == BidiClass.EuropeanNumber)
                classes[i] = BidiClass.EuropeanNumber;
            else if (classes[i] == BidiClass.CommonSeparator && classes[i - 1] == classes[i + 1] && (classes[i - 1] is BidiClass.EuropeanNumber or BidiClass.ArabicNumber))
                classes[i] = classes[i - 1];
        }

        for (int i = 0; i < classes.Length; i++)
        {
            if (classes[i] != BidiClass.EuropeanTerminator)
                continue;

            bool hasLeftEn = i > 0 && classes[i - 1] == BidiClass.EuropeanNumber;
            bool hasRightEn = i + 1 < classes.Length && classes[i + 1] == BidiClass.EuropeanNumber;
            if (hasLeftEn || hasRightEn)
                classes[i] = BidiClass.EuropeanNumber;
        }

        for (int i = 0; i < classes.Length; i++)
        {
            if (classes[i] is BidiClass.EuropeanSeparator or BidiClass.CommonSeparator or BidiClass.EuropeanTerminator)
                classes[i] = BidiClass.Neutral;
        }

        for (int i = 0; i < classes.Length; i++)
        {
            if (classes[i] != BidiClass.EuropeanNumber)
                continue;

            int previousStrong = FindPreviousStrong(classes, i - 1);
            if (previousStrong >= 0 && classes[previousStrong] == BidiClass.LeftToRight)
                classes[i] = BidiClass.LeftToRight;
        }
    }

    private static void ResolveNeutralTypes(BidiClass[] classes, byte[] levels, int paragraphLevel)
    {
        int i = 0;
        while (i < classes.Length)
        {
            if (!IsNeutralLike(classes[i]))
            {
                i++;
                continue;
            }

            int runStart = i;
            while (i < classes.Length && IsNeutralLike(classes[i]))
                i++;
            int runEnd = i - 1;

            BidiClass leftType = FindStrongType(classes, runStart - 1, -1, paragraphLevel);
            BidiClass rightType = FindStrongType(classes, runEnd + 1, 1, paragraphLevel);

            BidiClass resolved = leftType == rightType
                ? leftType
                : IsOdd(levels[runStart]) ? BidiClass.RightToLeft : BidiClass.LeftToRight;

            for (int j = runStart; j <= runEnd; j++)
                classes[j] = resolved;
        }
    }

    private static void ResolveImplicitLevels(BidiClass[] classes, byte[] levels, int paragraphLevel)
    {
        for (int i = 0; i < classes.Length; i++)
        {
            if (classes[i] is BidiClass.ParagraphSeparator or BidiClass.SegmentSeparator)
                continue;

            if (!IsOdd(levels[i]))
            {
                if (classes[i] == BidiClass.RightToLeft)
                    levels[i]++;
                else if (classes[i] is BidiClass.ArabicNumber or BidiClass.EuropeanNumber)
                    levels[i] += 2;
            }
            else
            {
                if (classes[i] is BidiClass.LeftToRight or BidiClass.ArabicNumber or BidiClass.EuropeanNumber)
                    levels[i]++;
            }
        }
    }

    private static void ApplyParagraphAndBoundaryResets(BidiClass[] classes, byte[] levels, int paragraphLevel)
    {
        byte paragraph = (byte)paragraphLevel;

        for (int i = 0; i < classes.Length; i++)
        {
            if (classes[i] is BidiClass.ParagraphSeparator or BidiClass.SegmentSeparator)
                levels[i] = paragraph;
        }

        for (int i = levels.Length - 1; i >= 0; i--)
        {
            if (classes[i] is BidiClass.WhiteSpace or BidiClass.LeftToRightIsolate or BidiClass.RightToLeftIsolate or BidiClass.FirstStrongIsolate or BidiClass.PopDirectionalIsolate)
            {
                levels[i] = paragraph;
                continue;
            }

            if (classes[i] is BidiClass.ParagraphSeparator or BidiClass.SegmentSeparator)
                continue;

            break;
        }
    }

    private static bool IsOdd(byte level) => (level & 1) == 1;

    private static bool IsNeutralLike(BidiClass bidiClass)
        => bidiClass is BidiClass.Neutral
        or BidiClass.WhiteSpace
        or BidiClass.BoundaryNeutral
        or BidiClass.LeftToRightIsolate
        or BidiClass.RightToLeftIsolate
        or BidiClass.FirstStrongIsolate
        or BidiClass.PopDirectionalIsolate;

    private static int FindPreviousVisible(BidiClass[] classes, int start)
    {
        for (int i = start; i >= 0; i--)
        {
            if (classes[i] is BidiClass.LeftToRightEmbedding
                or BidiClass.RightToLeftEmbedding
                or BidiClass.LeftToRightOverride
                or BidiClass.RightToLeftOverride
                or BidiClass.PopDirectionalFormat)
                continue;

            return i;
        }

        return -1;
    }

    private static int FindPreviousStrong(BidiClass[] classes, int start)
    {
        for (int i = start; i >= 0; i--)
        {
            if (classes[i] is BidiClass.LeftToRight or BidiClass.RightToLeft or BidiClass.ArabicLetter)
                return i;
        }

        return -1;
    }

    private static BidiClass FindStrongType(BidiClass[] classes, int start, int step, int paragraphLevel)
    {
        for (int i = start; i >= 0 && i < classes.Length; i += step)
        {
            if (classes[i] is BidiClass.LeftToRight)
                return BidiClass.LeftToRight;
            if (classes[i] is BidiClass.RightToLeft or BidiClass.ArabicLetter)
                return BidiClass.RightToLeft;
        }

        return paragraphLevel == 1 ? BidiClass.RightToLeft : BidiClass.LeftToRight;
    }

    private static void ReorderRunsByBidiLevels(List<TextRun> runs)
    {
        if (runs.Count <= 1)
            return;

        byte maxLevel = 0;
        byte minOddLevel = byte.MaxValue;

        for (int i = 0; i < runs.Count; i++)
        {
            byte level = runs[i].BidiLevel;
            if (level > maxLevel)
                maxLevel = level;
            if ((level & 1) == 1 && level < minOddLevel)
                minOddLevel = level;
        }

        if (minOddLevel == byte.MaxValue)
            return;

        for (int current = maxLevel; current >= minOddLevel; current--)
        {
            int i = 0;
            while (i < runs.Count)
            {
                if (runs[i].BidiLevel < current)
                {
                    i++;
                    continue;
                }

                int start = i;
                while (i < runs.Count && runs[i].BidiLevel >= current)
                    i++;

                runs.Reverse(start, i - start);
            }
        }
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

    private static ushort[] CollectLookupIndices(
        ScriptRecord[] scripts,
        FeatureRecord[] features,
        int lookupCount,
        string scriptTag,
        string? languageTag,
        IReadOnlyList<string> enabledFeatureTags)
    {
        return CollectLookupIndices(scripts, features, lookupCount, [scriptTag, "DFLT"], languageTag, enabledFeatureTags);
    }

    private static ushort[] CollectLookupIndices(
        ScriptRecord[] scripts,
        FeatureRecord[] features,
        int lookupCount,
        IReadOnlyList<string> scriptTags,
        string? languageTag,
        IReadOnlyList<string> enabledFeatureTags)
    {
        if (!TrySelectLangSys(scripts, scriptTags, languageTag, out var langSys))
            return [];

        var result = new List<ushort>(32);
        var seen = new HashSet<ushort>();
        var selectedFeatureIndices = new List<ushort>(langSys.FeatureIndices.Length + 1);

        if (langSys.RequestedFeatureIndex != 0xFFFF)
            selectedFeatureIndices.Add(langSys.RequestedFeatureIndex);

        for (int i = 0; i < langSys.FeatureIndices.Length; i++)
            selectedFeatureIndices.Add(langSys.FeatureIndices[i]);

        var indicesByTag = new Dictionary<string, List<ushort>>(StringComparer.Ordinal);
        for (int i = 0; i < selectedFeatureIndices.Count; i++)
        {
            ushort featureIndex = selectedFeatureIndices[i];
            if (featureIndex >= features.Length)
                continue;

            ref readonly var feature = ref features[featureIndex];
            if (!TryNormalizeFeatureTag(feature.Tag, out var normalizedFeatureTag))
                continue;

            if (!indicesByTag.TryGetValue(normalizedFeatureTag, out var indices))
            {
                indices = [];
                indicesByTag.Add(normalizedFeatureTag, indices);
            }

            indices.Add(featureIndex);
        }

        void AddFeatureLookupIndices(ushort featureIndex)
        {
            if (featureIndex >= features.Length)
                return;

            ref readonly var feature = ref features[featureIndex];

            if (!TryNormalizeFeatureTag(feature.Tag, out var normalizedFeatureTag) || !enabledFeatureTags.Contains(normalizedFeatureTag))
                return;

            var lookupIndices = feature.Feature.LookupListIndices;
            for (int i = 0; i < lookupIndices.Length; i++)
            {
                ushort lookupIndex = lookupIndices[i];
                if (lookupIndex >= lookupCount)
                    continue;
                if (seen.Add(lookupIndex))
                    result.Add(lookupIndex);
            }
        }

        for (int i = 0; i < enabledFeatureTags.Count; i++)
        {
            if (!indicesByTag.TryGetValue(enabledFeatureTags[i], out var featureIndices))
                continue;

            for (int j = 0; j < featureIndices.Count; j++)
                AddFeatureLookupIndices(featureIndices[j]);
        }

        return [.. result];
    }

    private static ushort[] GetCachedLookupIndices(
        string fontKey,
        string tableTag,
        ScriptRecord[] scripts,
        FeatureRecord[] features,
        int lookupCount,
        string scriptTag,
        string? languageTag,
        IReadOnlyList<string> enabledFeatureTags)
    {
        return GetCachedLookupIndices(fontKey, tableTag, scripts, features, lookupCount, [scriptTag, "DFLT"], languageTag, enabledFeatureTags);
    }

    private static ushort[] GetCachedLookupIndices(
        string fontKey,
        string tableTag,
        ScriptRecord[] scripts,
        FeatureRecord[] features,
        int lookupCount,
        IReadOnlyList<string> scriptTags,
        string? languageTag,
        IReadOnlyList<string> enabledFeatureTags)
    {
        string language = string.IsNullOrWhiteSpace(languageTag) ? string.Empty : languageTag.Trim().ToLowerInvariant();
        string scriptKey = string.Join('|', scriptTags);
        string featuresKey = string.Join(',', enabledFeatureTags);
        var cacheKey = new LookupPlanCacheKey(fontKey, tableTag, scriptKey, language, featuresKey);

        if (lookupPlanCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var computed = CollectLookupIndices(scripts, features, lookupCount, scriptTags, languageTag, enabledFeatureTags);
        lookupPlanCache.TryAdd(cacheKey, computed);
        return computed;
    }

    private static bool TrySelectLangSys(ScriptRecord[] scripts, string scriptTag, string? languageTag, out LangSys langSys)
        => TrySelectLangSys(scripts, [scriptTag, "DFLT"], languageTag, out langSys);

    private static bool TrySelectLangSys(ScriptRecord[] scripts, IReadOnlyList<string> scriptTags, string? languageTag, out LangSys langSys)
    {
        Script? selectedScript = null;

        for (int t = 0; t < scriptTags.Count && selectedScript is null; t++)
        {
            string wantedTag = scriptTags[t];
            for (int i = 0; i < scripts.Length; i++)
            {
                if (!string.Equals(scripts[i].Tag, wantedTag, StringComparison.OrdinalIgnoreCase))
                    continue;

                selectedScript = scripts[i].Script;
                break;
            }
        }

        if (selectedScript is null && scripts.Length > 0)
            selectedScript = scripts[0].Script;

        if (selectedScript is null)
        {
            langSys = default;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(languageTag))
        {
            for (int i = 0; i < selectedScript.Value.LangSysRecords.Length; i++)
            {
                if (!string.Equals(selectedScript.Value.LangSysRecords[i].Tag, languageTag, StringComparison.OrdinalIgnoreCase))
                    continue;

                langSys = selectedScript.Value.LangSysRecords[i].LangSys;
                return true;
            }
        }

        if (selectedScript.Value.DefaultLangSys is LangSys defaultLangSys)
        {
            langSys = defaultLangSys;
            return true;
        }

        if (selectedScript.Value.LangSysRecords.Length > 0)
        {
            langSys = selectedScript.Value.LangSysRecords[0].LangSys;
            return true;
        }

        langSys = default;
        return false;
    }

    private static void ReorderIndicGlyphs(TextShaperScript script, List<GlyphInfo> buffer)
    {
        if (!IsIndicScript(script) || buffer.Count < 2)
            return;

        for (int i = 1; i < buffer.Count; i++)
        {
            if (!IsPreBaseMatra(script, buffer[i].CodePoint))
                continue;

            int target = FindPreviousConsonantIndex(script, buffer, i - 1);
            if (target < 0 || target == i - 1)
                continue;

            var matra = buffer[i];
            buffer.RemoveAt(i);
            buffer.Insert(target, matra);
        }
    }

    private static bool IsIndicScript(TextShaperScript script)
        => script is TextShaperScript.Devanagari
        or TextShaperScript.Bengali
        or TextShaperScript.Gurmukhi
        or TextShaperScript.Gujarati
        or TextShaperScript.Oriya
        or TextShaperScript.Tamil
        or TextShaperScript.Telugu
        or TextShaperScript.Kannada
        or TextShaperScript.Malayalam
        or TextShaperScript.Sinhala;

    private static bool IsPreBaseMatra(TextShaperScript script, uint codePoint) => script switch
    {
        TextShaperScript.Devanagari => codePoint == 0x093F,
        TextShaperScript.Bengali => codePoint == 0x09BF,
        TextShaperScript.Gurmukhi => codePoint == 0x0A3F,
        TextShaperScript.Gujarati => codePoint == 0x0ABF,
        TextShaperScript.Oriya => codePoint == 0x0B3F,
        TextShaperScript.Tamil => codePoint == 0x0BBF,
        TextShaperScript.Telugu => codePoint == 0x0C3F,
        TextShaperScript.Kannada => codePoint == 0x0CBF,
        TextShaperScript.Malayalam => codePoint == 0x0D3F,
        TextShaperScript.Sinhala => codePoint == 0x0DD2,
        _ => false
    };

    private static int FindPreviousConsonantIndex(TextShaperScript script, List<GlyphInfo> buffer, int start)
    {
        for (int i = start; i >= 0; i--)
        {
            if (IsConsonant(script, buffer[i].CodePoint))
                return i;
        }

        return -1;
    }

    private static bool IsConsonant(TextShaperScript script, uint codePoint) => script switch
    {
        TextShaperScript.Devanagari => codePoint >= 0x0915 && codePoint <= 0x0939,
        TextShaperScript.Bengali => codePoint >= 0x0995 && codePoint <= 0x09B9,
        TextShaperScript.Gurmukhi => codePoint >= 0x0A15 && codePoint <= 0x0A39,
        TextShaperScript.Gujarati => codePoint >= 0x0A95 && codePoint <= 0x0AB9,
        TextShaperScript.Oriya => codePoint >= 0x0B15 && codePoint <= 0x0B39,
        TextShaperScript.Tamil => codePoint >= 0x0B95 && codePoint <= 0x0BB9,
        TextShaperScript.Telugu => codePoint >= 0x0C15 && codePoint <= 0x0C39,
        TextShaperScript.Kannada => codePoint >= 0x0C95 && codePoint <= 0x0CB9,
        TextShaperScript.Malayalam => codePoint >= 0x0D15 && codePoint <= 0x0D39,
        TextShaperScript.Sinhala => codePoint >= 0x0D9A && codePoint <= 0x0DC6,
        _ => false
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

    private static void ApplyGsub(GsubTable gsub, ushort[] lookupIndices, List<GlyphInfo> buffer, int recursionLimit)
    {
        for (int i = 0; i < lookupIndices.Length; i++)
            ApplyGsubLookupAt(gsub, lookupIndices[i], buffer, null, 0, recursionLimit);
    }

    private static void ApplyGsubByFeatureStages(
        string fontKey,
        GsubTable gsub,
        IReadOnlyList<string> scriptTags,
        string? languageTag,
        TextShaperScript script,
        IReadOnlyList<string> enabledFeatures,
        List<GlyphInfo> buffer,
        int recursionLimit)
    {
        var stages = BuildGsubFeatureStages(script, enabledFeatures);
        var appliedLookups = new HashSet<ushort>();

        for (int stageIndex = 0; stageIndex < stages.Count; stageIndex++)
        {
            var stageFeatures = stages[stageIndex];
            if (stageFeatures.Count == 0)
                continue;

            var stageLookupIndices = GetCachedLookupIndices(
                fontKey,
                "GSUB",
                gsub.ScriptList,
                gsub.FeatureList,
                gsub.LookupList.Length,
                scriptTags,
                languageTag,
                stageFeatures);

            if (stageLookupIndices.Length == 0)
                continue;

            var uniqueLookupIndices = FilterNewLookupIndices(stageLookupIndices, appliedLookups);
            if (uniqueLookupIndices.Length == 0)
                continue;

            ApplyGsub(gsub, uniqueLookupIndices, buffer, recursionLimit);
        }
    }

    private static ushort[] FilterNewLookupIndices(ushort[] lookupIndices, HashSet<ushort> seen)
    {
        var filtered = new List<ushort>(lookupIndices.Length);

        for (int i = 0; i < lookupIndices.Length; i++)
        {
            if (seen.Add(lookupIndices[i]))
                filtered.Add(lookupIndices[i]);
        }

        return [.. filtered];
    }

    private static List<List<string>> BuildGsubFeatureStages(TextShaperScript script, IReadOnlyList<string> enabledFeatures)
    {
        var templates = GetGsubFeatureStageTemplate(script);
        var enabledSet = new HashSet<string>(enabledFeatures, StringComparer.Ordinal);
        var usedFeatures = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<List<string>>(templates.Length + 1);

        for (int i = 0; i < templates.Length; i++)
        {
            var stage = new List<string>(templates[i].Length);
            for (int j = 0; j < templates[i].Length; j++)
            {
                string feature = templates[i][j];
                if (!enabledSet.Contains(feature))
                    continue;

                stage.Add(feature);
                usedFeatures.Add(feature);
            }

            result.Add(stage);
        }

        var fallbackStage = new List<string>();
        for (int i = 0; i < enabledFeatures.Count; i++)
        {
            string feature = enabledFeatures[i];
            if (!usedFeatures.Add(feature))
                continue;

            fallbackStage.Add(feature);
        }

        if (fallbackStage.Count > 0)
            result.Add(fallbackStage);

        return result;
    }

    private static string[][] GetGsubFeatureStageTemplate(TextShaperScript script) => script switch
    {
        TextShaperScript.Arabic =>
        [
            ["ccmp", "locl"],
            ["isol", "fina", "fin2", "fin3", "medi", "med2", "init"],
            ["rlig"],
            ["liga", "calt"]
        ],

        TextShaperScript.Syriac =>
        [
            ["ccmp", "locl"],
            ["isol", "fina", "fin2", "fin3", "medi", "med2", "init"],
            ["rlig"],
            ["liga", "calt"]
        ],

        TextShaperScript.Devanagari or
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
        TextShaperScript.Myanmar =>
        [
            ["ccmp", "locl", "nukt", "akhn"],
            ["rphf", "rkrf", "pref", "blwf", "abvf", "half", "pstf", "vatu", "cjct"],
            ["liga"]
        ],

        TextShaperScript.Tibetan =>
        [
            ["ccmp", "locl"],
            ["abvs", "blws"],
            ["liga"]
        ],

        TextShaperScript.Hangul =>
        [
            ["ccmp", "locl"],
            ["ljmo", "vjmo", "tjmo"],
            ["liga"]
        ],

        _ =>
        [
            ["ccmp", "locl"],
            ["liga", "clig", "calt"]
        ]
    };

    private static bool ApplyGsubLookupAt(GsubTable gsub, ushort lookupIndex, List<GlyphInfo> buffer, int? atIndex, int depth, int recursionLimit)
    {
        if (lookupIndex >= gsub.LookupList.Length || depth >= recursionLimit)
            return false;

        bool changed = false;
        var lookup = gsub.LookupList[lookupIndex];

        var previousContext = currentLookupFilterContext;
        currentLookupFilterContext = new LookupFilterContext(lookup.LookupFlag, lookup.MarkFilteringSet, null);
        try
        {
            for (int s = 0; s < lookup.Subtables.Length; s++)
                changed |= lookup.Subtables[s].Apply(gsub, buffer, atIndex, depth, recursionLimit);
        }
        finally
        {
            currentLookupFilterContext = previousContext;
        }

        return changed;
    }

    internal static bool ApplyGsubMatchedLookups(GsubTable gsub, SequenceMatch match, List<GlyphInfo> buffer, int depth, int recursionLimit)
    {
        bool changed = false;

        MergeClusters(buffer, match.Start, match.Length);

        var orderedLookups = (SequenceLookup[])match.Lookups.Clone();
        Array.Sort(orderedLookups, static (a, b) => a.SequenceIndex.CompareTo(b.SequenceIndex));

        int positionDelta = 0;
        for (int i = 0; i < orderedLookups.Length; i++)
        {
            int position = match.Start + orderedLookups[i].SequenceIndex + positionDelta;
            if ((uint)position >= (uint)buffer.Count)
                continue;

            int before = buffer.Count;
            if (ApplyGsubLookupAt(gsub, orderedLookups[i].LookupListIndex, buffer, position, depth, recursionLimit))
            {
                changed = true;
                positionDelta += buffer.Count - before;
            }
        }

        return changed;
    }

    internal static void MergeClusters(List<GlyphInfo> buffer, int start, int length)
    {
        if (buffer.Count == 0 || length <= 1)
            return;

        if (start < 0)
        {
            length += start;
            start = 0;
        }

        if (length <= 1 || start >= buffer.Count)
            return;

        int end = Math.Min(buffer.Count, start + length);
        int mergedCluster = buffer[start].Cluster;

        for (int i = start + 1; i < end; i++)
        {
            if (buffer[i].Cluster < mergedCluster)
                mergedCluster = buffer[i].Cluster;
        }

        for (int i = start; i < end; i++)
            buffer[i] = buffer[i] with { Cluster = mergedCluster };
    }

    private static void ApplyGpos(GposTable gpos, GdefTable? gdef, VariationInstance? variationInstance, ushort[] lookupIndices, List<GlyphInfo> buffer, GlyphAdjustment[] adjustments, int recursionLimit)
    {
        var previousVariation = currentVariationInstance;
        var previousStore = currentItemVariationStore;

        currentVariationInstance = variationInstance;
        currentItemVariationStore = gdef?.ItemVariationStore;

        try
        {
            for (int i = 0; i < lookupIndices.Length; i++)
                ApplyGposLookupAt(gpos, gdef, lookupIndices[i], buffer, adjustments, null, 0, recursionLimit);
        }
        finally
        {
            currentVariationInstance = previousVariation;
            currentItemVariationStore = previousStore;
        }
    }

    private static bool ApplyGposLookupAt(GposTable gpos, GdefTable? gdef, ushort lookupIndex, List<GlyphInfo> buffer, GlyphAdjustment[] adjustments, int? atIndex, int depth, int recursionLimit)
    {
        if (lookupIndex >= gpos.LookupList.Length || depth >= recursionLimit)
            return false;

        bool changed = false;
        var lookup = gpos.LookupList[lookupIndex];

        var previousContext = currentLookupFilterContext;
        currentLookupFilterContext = new LookupFilterContext(lookup.LookupFlag, lookup.MarkFilteringSet, gdef);
        try
        {
            for (int s = 0; s < lookup.Subtables.Length; s++)
                changed |= lookup.Subtables[s].Apply(gpos, buffer, adjustments, gdef, atIndex, depth, recursionLimit);
        }
        finally
        {
            currentLookupFilterContext = previousContext;
        }

        return changed;
    }

    internal static bool TryGetLookupFilterContext(out LookupFilterContext context)
    {
        if (currentLookupFilterContext is LookupFilterContext value)
        {
            context = value;
            return true;
        }

        context = default;
        return false;
    }

    private static double ResolveValueDelta(DeviceOrVariationIndexTable? deviceOrVariation)
    {
        if (deviceOrVariation is not VariationIndexTable variationIndex)
            return 0;
        if (currentItemVariationStore is not ItemVariationStore itemVariationStore || currentVariationInstance is not VariationInstance variationInstance)
            return 0;

        return itemVariationStore.Resolve(variationIndex, variationInstance);
    }

    internal static ushort[] ToLookupGlyphIds(List<GlyphInfo> buffer, out int[] indexMap)
    {
        if (!TryGetLookupFilterContext(out var context) || !HasLookupFiltering(context.LookupFlag))
        {
            indexMap = new int[buffer.Count];
            var glyphIds = new ushort[buffer.Count];
            for (int i = 0; i < buffer.Count; i++)
            {
                indexMap[i] = i;
                glyphIds[i] = buffer[i].GlyphId;
            }

            return glyphIds;
        }

        var glyphs = new List<ushort>(buffer.Count);
        var map = new List<int>(buffer.Count);

        for (int i = 0; i < buffer.Count; i++)
        {
            if (ShouldIgnoreForLookup(buffer[i].GlyphId, context))
                continue;

            map.Add(i);
            glyphs.Add(buffer[i].GlyphId);
        }

        indexMap = [.. map];
        return [.. glyphs];
    }

    internal static SequenceMatch RemapSequenceMatch(SequenceMatch filteredMatch, int[] indexMap, int rawBufferCount)
    {
        if (filteredMatch.Length <= 0 || filteredMatch.Start < 0 || filteredMatch.Start >= indexMap.Length)
            return filteredMatch;

        int filteredEnd = Math.Min(indexMap.Length, filteredMatch.Start + filteredMatch.Length) - 1;
        if (filteredEnd < filteredMatch.Start)
            return filteredMatch;

        int rawStart = indexMap[filteredMatch.Start];
        int rawEnd = indexMap[filteredEnd];
        int rawLength = Math.Min(rawBufferCount - rawStart, (rawEnd - rawStart) + 1);

        var remappedLookups = new SequenceLookup[filteredMatch.Lookups.Length];
        for (int i = 0; i < filteredMatch.Lookups.Length; i++)
        {
            int filteredSequencePosition = filteredMatch.Start + filteredMatch.Lookups[i].SequenceIndex;
            if ((uint)filteredSequencePosition >= (uint)indexMap.Length)
            {
                remappedLookups[i] = filteredMatch.Lookups[i];
                continue;
            }

            int rawPosition = indexMap[filteredSequencePosition];
            int rawSequenceIndex = Math.Max(0, rawPosition - rawStart);
            remappedLookups[i] = filteredMatch.Lookups[i] with
            {
                SequenceIndex = (ushort)Math.Min(rawSequenceIndex, ushort.MaxValue)
            };
        }

        return new SequenceMatch(rawStart, rawLength, remappedLookups);
    }

    private static bool HasLookupFiltering(LookupFlag lookupFlag)
        => lookupFlag.HasFlag(LookupFlag.IgnoreBaseGlyphs)
        || lookupFlag.HasFlag(LookupFlag.IgnoreLigatures)
        || lookupFlag.HasFlag(LookupFlag.IgnoreMarks)
        || lookupFlag.HasFlag(LookupFlag.UseMarkFilteringSet)
        || (((ushort)lookupFlag & (ushort)LookupFlag.MarkAttachmentClassFilter) != 0);

    private static bool ShouldIgnoreForLookup(ushort glyphId, LookupFilterContext context)
    {
        bool isMark = IsMarkGlyph(glyphId, context.Gdef);

        if (context.LookupFlag.HasFlag(LookupFlag.IgnoreMarks) && isMark)
            return true;
        if (context.LookupFlag.HasFlag(LookupFlag.IgnoreBaseGlyphs) && IsBaseGlyph(glyphId, context.Gdef))
            return true;
        if (context.LookupFlag.HasFlag(LookupFlag.IgnoreLigatures) && IsLigatureGlyph(glyphId, context.Gdef))
            return true;

        if (!isMark)
            return false;

        if (context.LookupFlag.HasFlag(LookupFlag.UseMarkFilteringSet))
        {
            if (context.Gdef?.MarkGlyphSets?.Coverages is not Coverage[] markGlyphSets)
                return true;
            if ((uint)context.MarkFilteringSet >= (uint)markGlyphSets.Length)
                return true;
            if (!markGlyphSets[context.MarkFilteringSet].TryGetIndex(glyphId, out _))
                return true;
        }

        ushort markAttachmentClass = (ushort)(((ushort)context.LookupFlag & (ushort)LookupFlag.MarkAttachmentClassFilter) >> 8);
        if (markAttachmentClass != 0)
        {
            if (context.Gdef?.MarkAttachClassDef is not ClassDef classDef)
                return true;
            if (!classDef.TryGetClass(glyphId, out ushort glyphClass) || glyphClass != markAttachmentClass)
                return true;
        }

        return false;
    }

    internal static bool ApplyGposMatchedLookups(GposTable gpos, GdefTable? gdef, SequenceMatch match, List<GlyphInfo> buffer, GlyphAdjustment[] adjustments, int depth, int recursionLimit)
    {
        bool changed = false;

        var orderedLookups = (SequenceLookup[])match.Lookups.Clone();
        Array.Sort(orderedLookups, static (a, b) => a.SequenceIndex.CompareTo(b.SequenceIndex));

        for (int i = 0; i < orderedLookups.Length; i++)
        {
            int position = match.Start + orderedLookups[i].SequenceIndex;
            if ((uint)position >= (uint)buffer.Count)
                continue;

            changed |= ApplyGposLookupAt(gpos, gdef, orderedLookups[i].LookupListIndex, buffer, adjustments, position, depth, recursionLimit);
        }

        return changed;
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

    private static BidiClass ClassifyBidi(Rune rune)
    {
        uint cp = (uint)rune.Value;

        return cp switch
        {
            0x202A => BidiClass.LeftToRightEmbedding,
            0x202B => BidiClass.RightToLeftEmbedding,
            0x202D => BidiClass.LeftToRightOverride,
            0x202E => BidiClass.RightToLeftOverride,
            0x202C => BidiClass.PopDirectionalFormat,
            0x2066 => BidiClass.LeftToRightIsolate,
            0x2067 => BidiClass.RightToLeftIsolate,
            0x2068 => BidiClass.FirstStrongIsolate,
            0x2069 => BidiClass.PopDirectionalIsolate,
            _ => ClassifyNonFormattingBidi(rune, cp)
        };
    }

    private static BidiClass ClassifyNonFormattingBidi(Rune rune, uint cp)
    {
        if (cp == 0x000A)
            return BidiClass.SegmentSeparator;
        if (cp == 0x000D || cp == 0x2029)
            return BidiClass.ParagraphSeparator;
        if (cp == 0x0009)
            return BidiClass.SegmentSeparator;
        if (cp <= 0x0020)
            return BidiClass.WhiteSpace;

        if (cp is >= 0x0590 and <= 0x05FF)
            return BidiClass.RightToLeft;
        if (cp is >= 0x0600 and <= 0x08FF)
            return BidiClass.ArabicLetter;
        if (cp is >= 0x0660 and <= 0x0669)
            return BidiClass.ArabicNumber;
        if (cp is >= 0x0030 and <= 0x0039)
            return BidiClass.EuropeanNumber;
        if (cp is 0x002B or 0x002D)
            return BidiClass.EuropeanSeparator;
        if (cp is 0x002C or 0x002E or 0x003A)
            return BidiClass.CommonSeparator;
        if (cp is 0x0023 or 0x0024 or 0x066A)
            return BidiClass.EuropeanTerminator;
        if (cp is 0x200C or 0x200D)
            return BidiClass.BoundaryNeutral;

        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.NonSpacingMark => BidiClass.NonSpacingMark,
            UnicodeCategory.SpacingCombiningMark => BidiClass.NonSpacingMark,
            UnicodeCategory.DecimalDigitNumber => BidiClass.EuropeanNumber,
            UnicodeCategory.SpaceSeparator => BidiClass.WhiteSpace,
            UnicodeCategory.LineSeparator => BidiClass.SegmentSeparator,
            UnicodeCategory.ParagraphSeparator => BidiClass.ParagraphSeparator,
            UnicodeCategory.ConnectorPunctuation => BidiClass.Neutral,
            UnicodeCategory.DashPunctuation => BidiClass.Neutral,
            UnicodeCategory.OpenPunctuation => BidiClass.Neutral,
            UnicodeCategory.ClosePunctuation => BidiClass.Neutral,
            UnicodeCategory.InitialQuotePunctuation => BidiClass.Neutral,
            UnicodeCategory.FinalQuotePunctuation => BidiClass.Neutral,
            UnicodeCategory.OtherPunctuation => BidiClass.Neutral,
            UnicodeCategory.Format => BidiClass.BoundaryNeutral,
            _ => IsRightToLeftScript(ClassifyScript(cp)) ? BidiClass.RightToLeft : BidiClass.LeftToRight
        };
    }

    private static TextShaperScript ClassifyScript(uint codePoint)
    {
        if (codePoint <= 0x024F)
            return TextShaperScript.Latin;

        return codePoint switch
        {
            >= 0x0590 and <= 0x05FF => TextShaperScript.Hebrew,
            >= 0x0600 and <= 0x06FF => TextShaperScript.Arabic,
            >= 0x0750 and <= 0x077F => TextShaperScript.Arabic,
            >= 0x08A0 and <= 0x08FF => TextShaperScript.Arabic,
            >= 0x0700 and <= 0x074F => TextShaperScript.Syriac,
            >= 0x0900 and <= 0x097F => TextShaperScript.Devanagari,
            >= 0x0980 and <= 0x09FF => TextShaperScript.Bengali,
            >= 0x0A00 and <= 0x0A7F => TextShaperScript.Gurmukhi,
            >= 0x0A80 and <= 0x0AFF => TextShaperScript.Gujarati,
            >= 0x0B00 and <= 0x0B7F => TextShaperScript.Oriya,
            >= 0x0B80 and <= 0x0BFF => TextShaperScript.Tamil,
            >= 0x0C00 and <= 0x0C7F => TextShaperScript.Telugu,
            >= 0x0C80 and <= 0x0CFF => TextShaperScript.Kannada,
            >= 0x0D00 and <= 0x0D7F => TextShaperScript.Malayalam,
            >= 0x0D80 and <= 0x0DFF => TextShaperScript.Sinhala,
            >= 0x0E00 and <= 0x0E7F => TextShaperScript.Thai,
            >= 0x0E80 and <= 0x0EFF => TextShaperScript.Lao,
            >= 0x0F00 and <= 0x0FFF => TextShaperScript.Tibetan,
            >= 0x1000 and <= 0x109F => TextShaperScript.Myanmar,
            >= 0x1780 and <= 0x17FF => TextShaperScript.Khmer,
            >= 0x1100 and <= 0x11FF => TextShaperScript.Hangul,
            >= 0x3130 and <= 0x318F => TextShaperScript.Hangul,
            >= 0xA960 and <= 0xA97F => TextShaperScript.Hangul,
            >= 0xAC00 and <= 0xD7A3 => TextShaperScript.Hangul,
            >= 0xD7B0 and <= 0xD7FF => TextShaperScript.Hangul,
            _ => TextShaperScript.Unknown
        };
    }

    private enum BidiClass
    {
        LeftToRightEmbedding,
        RightToLeftEmbedding,
        LeftToRightOverride,
        RightToLeftOverride,
        PopDirectionalFormat,
        LeftToRightIsolate,
        RightToLeftIsolate,
        FirstStrongIsolate,
        PopDirectionalIsolate,
        BoundaryNeutral,
        LeftToRight,
        RightToLeft,
        ArabicLetter,
        EuropeanNumber,
        ArabicNumber,
        EuropeanSeparator,
        EuropeanTerminator,
        CommonSeparator,
        NonSpacingMark,
        ParagraphSeparator,
        SegmentSeparator,
        WhiteSpace,
        Neutral
    }

    private enum GraphemeBreakClass
    {
        Other,
        CR,
        LF,
        Control,
        Extend,
        ZWJ,
        RegionalIndicator,
        Prepend,
        SpacingMark,
        L,
        V,
        T,
        LV,
        LVT
    }

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
