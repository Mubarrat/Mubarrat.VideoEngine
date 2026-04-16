namespace Mubarrat.OpenType.CommonTables;

public sealed record ChainedSequenceContextFormat1(Coverage Coverage, ChainedSequenceRuleSet[] RuleSets) : ChainedSequenceContext
{
    public static ChainedSequenceContextFormat1 Parse(OpenTypeReader.TableScope scope) => new(
        Coverage: scope.ParseCommonTable<Coverage>(scope.Reader.ReadOffset16()),
        RuleSets: ChainedSequenceRuleSet.ParseListFromOffsets16(scope));

    internal override bool TryMatchChainedContext(ushort[] glyphs, int startIndex, out SequenceMatch match)
    {
        match = default;

        if ((uint)startIndex >= (uint)glyphs.Length)
            return false;

        if (!Coverage.TryGetIndex(glyphs[startIndex], out ushort coverageIndex))
            return false;

        if (coverageIndex >= RuleSets.Length)
            return false;

        var rules = RuleSets[coverageIndex].Rules;

        for (int r = 0; r < rules.Length; r++)
        {
            var rule = rules[r];
            int backtrackCount = rule.BacktrackCoverages.Length;
            int inputCount = rule.InputCoverages.Length + 1;
            int lookaheadCount = rule.LookaheadCoverages.Length;

            if (startIndex - backtrackCount < 0)
                continue;
            if (startIndex + inputCount + lookaheadCount > glyphs.Length)
                continue;

            bool valid = true;

            for (int i = 0; i < backtrackCount; i++)
            {
                if (!rule.BacktrackCoverages[i].TryGetIndex(glyphs[startIndex - i - 1], out _))
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
                continue;

            for (int i = 0; i < rule.InputCoverages.Length; i++)
            {
                if (!rule.InputCoverages[i].TryGetIndex(glyphs[startIndex + i + 1], out _))
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
                continue;

            for (int i = 0; i < lookaheadCount; i++)
            {
                if (!rule.LookaheadCoverages[i].TryGetIndex(glyphs[startIndex + inputCount + i], out _))
                {
                    valid = false;
                    break;
                }
            }

            if (!valid)
                continue;

            match = new SequenceMatch(startIndex, inputCount, rule.Lookups);
            return true;
        }

        return false;
    }
}
