using Mubarrat.OpenType.Tables;
using System.Globalization;
using System.Text;

namespace Mubarrat.OpenType.TextShaping;

public static partial class OpenTypeTextShaper
{
    private static void ReorderIndicGlyphs(TextShaperScript script, List<GlyphInfo> buffer)
    {
        if (!IsIndicScript(script) || buffer.Count < 2)
            return;

        int syllableStart = 0;
        while (syllableStart < buffer.Count)
        {
            int syllableEnd = FindIndicSyllableEnd(script, buffer, syllableStart);
            ReorderIndicSyllable(script, buffer, syllableStart, syllableEnd);
            syllableStart = syllableEnd;
        }
    }

    private static int FindIndicSyllableEnd(TextShaperScript script, List<GlyphInfo> buffer, int start)
    {
        int cluster = buffer[start].Cluster;
        int i = start;
        IndicSyllableState state = IndicSyllableState.Start;

        while (i < buffer.Count && buffer[i].Cluster == cluster)
        {
            state = AdvanceIndicSyllableState(script, state, buffer[i].CodePoint);
            i++;
        }

        return i;
    }

    private static IndicSyllableState AdvanceIndicSyllableState(TextShaperScript script, IndicSyllableState state, uint codePoint)
    {
        var category = ClassifyIndicCategory(script, codePoint);
        return (state, category) switch
        {
            (IndicSyllableState.Start, IndicCategory.Consonant) => IndicSyllableState.ConsonantCluster,
            (IndicSyllableState.Start, IndicCategory.VowelIndependent) => IndicSyllableState.IndependentVowel,
            (IndicSyllableState.Start, IndicCategory.Nukta) => IndicSyllableState.ModifierSequence,
            (IndicSyllableState.Start, IndicCategory.MatraPre) => IndicSyllableState.MatraSequence,
            (IndicSyllableState.Start, IndicCategory.MatraPost) => IndicSyllableState.MatraSequence,
            (IndicSyllableState.Start, IndicCategory.Sign) => IndicSyllableState.ModifierSequence,
            (IndicSyllableState.Start, IndicCategory.Joiner) => IndicSyllableState.ModifierSequence,
            (IndicSyllableState.Start, IndicCategory.Virama) => IndicSyllableState.ModifierSequence,

            (IndicSyllableState.ConsonantCluster, IndicCategory.Nukta) => IndicSyllableState.ConsonantCluster,
            (IndicSyllableState.ConsonantCluster, IndicCategory.Virama) => IndicSyllableState.AfterVirama,
            (IndicSyllableState.ConsonantCluster, IndicCategory.MatraPre) => IndicSyllableState.MatraSequence,
            (IndicSyllableState.ConsonantCluster, IndicCategory.MatraPost) => IndicSyllableState.MatraSequence,
            (IndicSyllableState.ConsonantCluster, IndicCategory.Sign) => IndicSyllableState.ModifierSequence,
            (IndicSyllableState.ConsonantCluster, IndicCategory.Joiner) => IndicSyllableState.ModifierSequence,

            (IndicSyllableState.AfterVirama, IndicCategory.Consonant) => IndicSyllableState.ConsonantCluster,
            (IndicSyllableState.AfterVirama, IndicCategory.Joiner) => IndicSyllableState.AfterVirama,
            (IndicSyllableState.AfterVirama, IndicCategory.Sign) => IndicSyllableState.ModifierSequence,

            (IndicSyllableState.IndependentVowel, IndicCategory.Sign) => IndicSyllableState.ModifierSequence,
            (IndicSyllableState.IndependentVowel, IndicCategory.MatraPre) => IndicSyllableState.MatraSequence,
            (IndicSyllableState.IndependentVowel, IndicCategory.MatraPost) => IndicSyllableState.MatraSequence,

            (IndicSyllableState.MatraSequence, IndicCategory.MatraPre) => IndicSyllableState.MatraSequence,
            (IndicSyllableState.MatraSequence, IndicCategory.MatraPost) => IndicSyllableState.MatraSequence,
            (IndicSyllableState.MatraSequence, IndicCategory.Sign) => IndicSyllableState.ModifierSequence,
            (IndicSyllableState.MatraSequence, IndicCategory.Joiner) => IndicSyllableState.ModifierSequence,

            (IndicSyllableState.ModifierSequence, IndicCategory.Sign) => IndicSyllableState.ModifierSequence,
            (IndicSyllableState.ModifierSequence, IndicCategory.Joiner) => IndicSyllableState.ModifierSequence,
            (IndicSyllableState.ModifierSequence, IndicCategory.Nukta) => IndicSyllableState.ModifierSequence,
            (IndicSyllableState.ModifierSequence, IndicCategory.Virama) => IndicSyllableState.AfterVirama,

            _ => IndicSyllableState.Broken
        };
    }

    private static void ReorderIndicSyllable(TextShaperScript script, List<GlyphInfo> buffer, int start, int end)
    {
        if (end - start < 2)
            return;

        int rephLength = GetRephPrefixLength(script, buffer, start, end);
        int baseConsonantIndex = FindBaseConsonantIndex(script, buffer, start + rephLength, end);
        if (baseConsonantIndex < 0)
            return;

        for (int i = baseConsonantIndex + 1; i < end; i++)
        {
            if (GetIndicMatraPosition(script, buffer[i].CodePoint) != IndicMatraPosition.Pre)
                continue;

            int target = FindPreBaseInsertionIndex(script, buffer, start, rephLength, baseConsonantIndex);
            if (target < start || target == i)
                continue;

            var matra = buffer[i];
            buffer.RemoveAt(i);
            buffer.Insert(target, matra);

            if (target <= baseConsonantIndex)
                baseConsonantIndex++;

            i--;
        }

        if (rephLength > 0)
        {
            int rephTarget = FindRephTargetIndex(script, buffer, baseConsonantIndex + 1, end);
            MoveGlyphRange(buffer, start, rephLength, rephTarget);
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
        TextShaperScript.Devanagari => codePoint is 0x093F or 0x0946 or 0x0947 or 0x0948,
        TextShaperScript.Bengali => codePoint is 0x09BF or 0x09C7 or 0x09C8,
        TextShaperScript.Gurmukhi => codePoint is 0x0A3F or 0x0A47 or 0x0A48,
        TextShaperScript.Gujarati => codePoint is 0x0ABF or 0x0AC5 or 0x0AC7 or 0x0AC8,
        TextShaperScript.Oriya => codePoint is 0x0B3F or 0x0B47 or 0x0B48,
        TextShaperScript.Tamil => codePoint is 0x0BC6 or 0x0BC7 or 0x0BC8,
        TextShaperScript.Telugu => codePoint is 0x0C46 or 0x0C47 or 0x0C48,
        TextShaperScript.Kannada => codePoint is 0x0CBF or 0x0CC6 or 0x0CC7 or 0x0CC8,
        TextShaperScript.Malayalam => codePoint is 0x0D3F or 0x0D46 or 0x0D47 or 0x0D48,
        TextShaperScript.Sinhala => codePoint is 0x0DD2 or 0x0DD9 or 0x0DDA,
        _ => false
    };

    private static void ExpandIndicSplitMatras(TextShaperScript script, List<GlyphInfo> buffer, CmapTable cmap)
    {
        for (int i = 0; i < buffer.Count; i++)
        {
            uint codePoint = buffer[i].CodePoint;
            if (!TryGetSplitMatraParts(script, codePoint, out uint prePart, out uint postPart))
                continue;

            if (!cmap.TryGetGlyphId(prePart, out ushort preGlyphId) || preGlyphId == 0)
                continue;

            if (!cmap.TryGetGlyphId(postPart, out ushort postGlyphId) || postGlyphId == 0)
                continue;

            int cluster = buffer[i].Cluster;
            buffer[i] = new GlyphInfo(preGlyphId, prePart, cluster);
            buffer.Insert(i + 1, new GlyphInfo(postGlyphId, postPart, cluster));
            i++;
        }
    }

    private static bool TryGetSplitMatraParts(TextShaperScript script, uint codePoint, out uint prePart, out uint postPart)
    {
        switch (script)
        {
            case TextShaperScript.Bengali:
                if (codePoint == 0x09CB)
                {
                    prePart = 0x09C7;
                    postPart = 0x09BE;
                    return true;
                }

                if (codePoint == 0x09CC)
                {
                    prePart = 0x09C7;
                    postPart = 0x09D7;
                    return true;
                }

                break;
        }

        prePart = 0;
        postPart = 0;
        return false;
    }

    private static void InsertDottedCircleForBrokenIndicSyllables(TextShaperScript script, List<GlyphInfo> buffer, CmapTable cmap)
    {
        const uint dottedCircleCodePoint = 0x25CC;
        if (!cmap.TryGetGlyphId(dottedCircleCodePoint, out ushort dottedCircleGlyphId) || dottedCircleGlyphId == 0)
            return;

        for (int start = 0; start < buffer.Count;)
        {
            int end = FindIndicSyllableEnd(script, buffer, start);
            if (!ShouldInsertDottedCircle(script, buffer, start, end))
            {
                start = end;
                continue;
            }

            int cluster = buffer[start].Cluster;
            buffer.Insert(start, new GlyphInfo(dottedCircleGlyphId, dottedCircleCodePoint, cluster));
            start = end + 1;
        }
    }

    private static bool ShouldInsertDottedCircle(TextShaperScript script, List<GlyphInfo> buffer, int start, int end)
    {
        bool hasBase = false;
        bool hasDependent = false;

        for (int i = start; i < end; i++)
        {
            uint codePoint = buffer[i].CodePoint;
            if (IsConsonant(script, codePoint) || IsIndependentVowel(script, codePoint))
                hasBase = true;

            if (IsVirama(script, codePoint)
                || IsIndicNukta(script, codePoint)
                || GetIndicMatraPosition(script, codePoint) != IndicMatraPosition.None
                || IsIndicCombiningMark(codePoint))
            {
                hasDependent = true;
            }
        }

        return !hasBase && hasDependent;
    }

    private static IndicCategory ClassifyIndicCategory(TextShaperScript script, uint codePoint)
    {
        if (IsConsonant(script, codePoint))
            return IndicCategory.Consonant;
        if (IsIndependentVowel(script, codePoint))
            return IndicCategory.VowelIndependent;
        if (IsVirama(script, codePoint))
            return IndicCategory.Virama;
        if (IsIndicNukta(script, codePoint))
            return IndicCategory.Nukta;

        return GetIndicMatraPosition(script, codePoint) switch
        {
            IndicMatraPosition.Pre => IndicCategory.MatraPre,
            IndicMatraPosition.Post => IndicCategory.MatraPost,
            _ when IsJoinControl(codePoint) => IndicCategory.Joiner,
            _ when IsIndicCombiningMark(codePoint) => IndicCategory.Sign,
            _ => IndicCategory.Other
        };
    }

    private static int GetRephPrefixLength(TextShaperScript script, List<GlyphInfo> buffer, int start, int end)
    {
        if (end - start < 3)
            return 0;

        if (!IsRa(script, buffer[start].CodePoint))
            return 0;

        int index = start + 1;
        if (index < end && IsIndicNukta(script, buffer[index].CodePoint))
            index++;

        if (index >= end || !IsVirama(script, buffer[index].CodePoint))
            return 0;

        index++;
        if (index >= end || !ContainsConsonant(script, buffer, index, end))
            return 0;

        return index - start;
    }

    private static bool ContainsConsonant(TextShaperScript script, List<GlyphInfo> buffer, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (IsConsonant(script, buffer[i].CodePoint))
                return true;
        }

        return false;
    }

    private static int FindBaseConsonantIndex(TextShaperScript script, List<GlyphInfo> buffer, int start, int end)
    {
        for (int i = end - 1; i >= start; i--)
        {
            if (IsConsonant(script, buffer[i].CodePoint))
                return i;
        }

        return -1;
    }

    private static int FindPreBaseInsertionIndex(TextShaperScript script, List<GlyphInfo> buffer, int syllableStart, int rephPrefixLength, int baseConsonantIndex)
    {
        int insertionIndex = baseConsonantIndex;
        while (insertionIndex >= syllableStart + 2
            && IsVirama(script, buffer[insertionIndex - 1].CodePoint)
            && IsConsonant(script, buffer[insertionIndex - 2].CodePoint))
        {
            insertionIndex -= 2;
        }

        int minimum = syllableStart + rephPrefixLength;
        if (insertionIndex < minimum)
            insertionIndex = minimum;

        return insertionIndex;
    }

    private static int FindRephTargetIndex(TextShaperScript script, List<GlyphInfo> buffer, int start, int end)
    {
        if (script == TextShaperScript.Bengali)
            return FindBengaliRephTargetIndex(buffer, start, end);

        int target = end;
        while (target > start)
        {
            uint codePoint = buffer[target - 1].CodePoint;
            if (!IsJoinControl(codePoint)
                && !IsIndicNukta(script, codePoint)
                && !IsVirama(script, codePoint)
                && !IsIndicCombiningMark(codePoint))
            {
                break;
            }

            target--;
        }

        return target;
    }

    private static int FindBengaliRephTargetIndex(List<GlyphInfo> buffer, int start, int end)
    {
        int target = start;

        for (int i = start; i < end; i++)
        {
            uint codePoint = buffer[i].CodePoint;
            if (IsConsonant(TextShaperScript.Bengali, codePoint))
            {
                target = i + 1;
                continue;
            }

            if (GetIndicMatraPosition(TextShaperScript.Bengali, codePoint) != IndicMatraPosition.None
                || IsIndicCombiningMark(codePoint))
            {
                break;
            }
        }

        return Math.Clamp(target, start, end);
    }

    private static void MoveGlyphRange(List<GlyphInfo> buffer, int start, int length, int target)
    {
        if (length <= 0 || start < 0 || start + length > buffer.Count)
            return;

        if (target >= start && target <= start + length)
            return;

        var segment = buffer.GetRange(start, length);
        buffer.RemoveRange(start, length);

        if (target > start)
            target -= length;

        buffer.InsertRange(target, segment);
    }

    private static bool IsVirama(TextShaperScript script, uint codePoint) => script switch
    {
        TextShaperScript.Devanagari => codePoint == 0x094D,
        TextShaperScript.Bengali => codePoint == 0x09CD,
        TextShaperScript.Gurmukhi => codePoint == 0x0A4D,
        TextShaperScript.Gujarati => codePoint == 0x0ACD,
        TextShaperScript.Oriya => codePoint == 0x0B4D,
        TextShaperScript.Tamil => codePoint == 0x0BCD,
        TextShaperScript.Telugu => codePoint == 0x0C4D,
        TextShaperScript.Kannada => codePoint == 0x0CCD,
        TextShaperScript.Malayalam => codePoint == 0x0D4D,
        TextShaperScript.Sinhala => codePoint == 0x0DCA,
        _ => false
    };

    private static IndicMatraPosition GetIndicMatraPosition(TextShaperScript script, uint codePoint)
    {
        if (IsPreBaseMatra(script, codePoint))
            return IndicMatraPosition.Pre;

        if (IsIndicCombiningMark(codePoint)
            && !IsVirama(script, codePoint)
            && !IsIndicNukta(script, codePoint))
        {
            return IndicMatraPosition.Post;
        }

        return IndicMatraPosition.None;
    }

    private static bool IsIndicNukta(TextShaperScript script, uint codePoint) => script switch
    {
        TextShaperScript.Devanagari => codePoint == 0x093C,
        TextShaperScript.Bengali => codePoint == 0x09BC,
        TextShaperScript.Gurmukhi => codePoint == 0x0A3C,
        TextShaperScript.Gujarati => codePoint == 0x0ABC,
        TextShaperScript.Oriya => codePoint == 0x0B3C,
        TextShaperScript.Kannada => codePoint == 0x0CBC,
        _ => false
    };

    private static bool IsIndependentVowel(TextShaperScript script, uint codePoint) => script switch
    {
        TextShaperScript.Devanagari => codePoint >= 0x0904 && codePoint <= 0x0914,
        TextShaperScript.Bengali => codePoint >= 0x0985 && codePoint <= 0x0994,
        TextShaperScript.Gurmukhi => codePoint >= 0x0A05 && codePoint <= 0x0A14,
        TextShaperScript.Gujarati => codePoint >= 0x0A85 && codePoint <= 0x0A94,
        TextShaperScript.Oriya => codePoint >= 0x0B05 && codePoint <= 0x0B14,
        TextShaperScript.Tamil => codePoint >= 0x0B85 && codePoint <= 0x0B94,
        TextShaperScript.Telugu => codePoint >= 0x0C05 && codePoint <= 0x0C14,
        TextShaperScript.Kannada => codePoint >= 0x0C85 && codePoint <= 0x0C94,
        TextShaperScript.Malayalam => codePoint >= 0x0D05 && codePoint <= 0x0D14,
        TextShaperScript.Sinhala => codePoint >= 0x0D85 && codePoint <= 0x0D96,
        _ => false
    };

    private static bool IsRa(TextShaperScript script, uint codePoint) => script switch
    {
        TextShaperScript.Devanagari => codePoint == 0x0930,
        TextShaperScript.Bengali => codePoint == 0x09B0,
        TextShaperScript.Gurmukhi => codePoint == 0x0A30,
        TextShaperScript.Gujarati => codePoint == 0x0AB0,
        TextShaperScript.Oriya => codePoint == 0x0B30,
        TextShaperScript.Tamil => codePoint == 0x0BB0,
        TextShaperScript.Telugu => codePoint == 0x0C30,
        TextShaperScript.Kannada => codePoint == 0x0CB0,
        TextShaperScript.Malayalam => codePoint == 0x0D30,
        TextShaperScript.Sinhala => codePoint == 0x0DBB,
        _ => false
    };

    private static bool IsIndicCombiningMark(uint codePoint)
    {
        if (!Rune.TryCreate((int)codePoint, out var rune))
            return false;

        var category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark;
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

    private enum IndicSyllableState
    {
        Start,
        ConsonantCluster,
        AfterVirama,
        IndependentVowel,
        MatraSequence,
        ModifierSequence,
        Broken
    }

    private enum IndicCategory
    {
        Other,
        Consonant,
        VowelIndependent,
        Nukta,
        Virama,
        Joiner,
        MatraPre,
        MatraPost,
        Sign
    }

    private enum IndicMatraPosition
    {
        None,
        Pre,
        Post
    }
}
