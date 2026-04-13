namespace Mubarrat.OpenType.CommonTables;

public sealed record ChainedSequenceRuleSet(
    ChainedSequenceRule[] Rules) : IOpenTypeCommonTable<ChainedSequenceRuleSet>
{
    public static ChainedSequenceRuleSet Parse(OpenTypeReader.TableScope scope, object? param = null) => new(ChainedSequenceRule.ParseListFromOffsets16(scope, param));
}
