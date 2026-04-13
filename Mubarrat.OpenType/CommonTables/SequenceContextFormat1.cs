namespace Mubarrat.OpenType.CommonTables;

public sealed record SequenceContextFormat1(
    Coverage Coverage,
    SequenceRuleSet[] RuleSets) : SequenceContext
{
    public static SequenceContextFormat1 Parse(OpenTypeReader.TableScope scope) => new(
        Coverage: scope.ParseCommonTable<Coverage>(scope.Reader.ReadOffset16()),
        RuleSets: SequenceRuleSet.ParseListFromOffsets16(scope));

    public override bool TryMatch(ushort[] glyphs, int startIndex, out SequenceMatch match)
    {
        match = default;

        if (!Coverage.TryGetIndex(glyphs[startIndex], out var index))
            return false;

        return RuleSets[index].TryMatch(glyphs, startIndex, out match);
    }
}
