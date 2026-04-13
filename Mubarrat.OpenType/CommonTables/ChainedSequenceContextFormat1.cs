namespace Mubarrat.OpenType.CommonTables;

public sealed record ChainedSequenceContextFormat1(Coverage Coverage, ChainedSequenceRuleSet[] RuleSets) : ChainedSequenceContext
{
    public static ChainedSequenceContextFormat1 Parse(OpenTypeReader.TableScope scope) => new(
        Coverage: scope.ParseCommonTable<Coverage>(scope.Reader.ReadOffset16()),
        RuleSets: ChainedSequenceRuleSet.ParseListFromOffsets16(scope));
}
