namespace Mubarrat.OpenType.CommonTables;

public sealed record ChainedSequenceContextFormat2(
    Coverage Coverage,
    ClassDef BacktrackClassDef,
    ClassDef InputClassDef,
    ClassDef LookaheadClassDef,
    ChainedClassSequenceRuleSet[] ChainedClassSeqRuleSets) : ChainedSequenceContext
{
    public static ChainedSequenceContextFormat2 Parse(OpenTypeReader.TableScope scope) => new(
        Coverage: scope.ParseCommonTable<Coverage>(scope.Reader.ReadOffset16()),
        BacktrackClassDef: scope.ParseCommonTable<ClassDef>(scope.Reader.ReadOffset16()),
        InputClassDef: scope.ParseCommonTable<ClassDef>(scope.Reader.ReadOffset16()),
        LookaheadClassDef: scope.ParseCommonTable<ClassDef>(scope.Reader.ReadOffset16()),
        ChainedClassSeqRuleSets: ChainedClassSequenceRuleSet.ParseListFromOffsets16(scope));

    internal override bool TryMatchChainedContext(ushort[] glyphs, int startIndex, out SequenceMatch match)
    {
        match = default;

        if ((uint)startIndex >= (uint)glyphs.Length)
            return false;

        if (!Coverage.TryGetIndex(glyphs[startIndex], out _))
            return false;

        ushort firstClass = InputClassDef.TryGetClass(glyphs[startIndex], out var first) ? first : (ushort)0;
        if (firstClass >= ChainedClassSeqRuleSets.Length)
            return false;

        var rules = ChainedClassSeqRuleSets[firstClass].Rules;

        for (int r = 0; r < rules.Length; r++)
        {
            var rule = rules[r];

            int backtrackCount = rule.BacktrackSequence.Length;
            int inputCount = rule.InputSequence.Length + 1;
            int lookaheadCount = rule.LookaheadSequence.Length;

            if (startIndex - backtrackCount < 0)
                continue;
            if (startIndex + inputCount + lookaheadCount > glyphs.Length)
                continue;

            bool valid = true;

            for (int i = 0; i < backtrackCount; i++)
            {
                ushort cls = BacktrackClassDef.TryGetClass(glyphs[startIndex - i - 1], out var c) ? c : (ushort)0;
                if (cls != rule.BacktrackSequence[i])
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
                continue;

            for (int i = 0; i < rule.InputSequence.Length; i++)
            {
                ushort cls = InputClassDef.TryGetClass(glyphs[startIndex + i + 1], out var c) ? c : (ushort)0;
                if (cls != rule.InputSequence[i])
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
                continue;

            for (int i = 0; i < lookaheadCount; i++)
            {
                ushort cls = LookaheadClassDef.TryGetClass(glyphs[startIndex + inputCount + i], out var c) ? c : (ushort)0;
                if (cls != rule.LookaheadSequence[i])
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
                continue;

            match = new SequenceMatch(startIndex, inputCount, rule.SeqLookupRecords);
            return true;
        }

        return false;
    }
}
