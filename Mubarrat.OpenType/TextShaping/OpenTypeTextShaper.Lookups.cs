using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.Tables;

namespace Mubarrat.OpenType.TextShaping;

public static partial class OpenTypeTextShaper
{
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
}
