namespace Mubarrat.VideoEngine.OpenType.CommonTables;

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
}
