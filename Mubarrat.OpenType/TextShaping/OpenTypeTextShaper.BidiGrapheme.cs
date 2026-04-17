using System.Globalization;
using System.Text;

namespace Mubarrat.OpenType.TextShaping;

public static partial class OpenTypeTextShaper
{
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
}
