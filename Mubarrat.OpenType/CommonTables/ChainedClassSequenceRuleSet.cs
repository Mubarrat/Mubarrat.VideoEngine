namespace Mubarrat.OpenType.CommonTables;

public readonly record struct ChainedClassSequenceRuleSet(ChainedClassSequenceRule[] Rules) : IOpenTypeCommonTable<ChainedClassSequenceRuleSet>
{
    public static ChainedClassSequenceRuleSet Parse(OpenTypeReader.TableScope scope, object? param = null) => new(ChainedClassSequenceRule.ParseListFromOffsets16(scope, param));
}
