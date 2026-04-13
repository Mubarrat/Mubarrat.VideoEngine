namespace Mubarrat.OpenType.CommonTables;

public sealed record SequenceContextFormat2(
    Coverage Coverage,
    ClassDef ClassDef,
    ClassSequenceRuleSet[] RuleSets) : SequenceContext
{
    public static SequenceContextFormat2 Parse(OpenTypeReader.TableScope scope) => new(
        Coverage: scope.ParseCommonTable<Coverage>(scope.Reader.ReadOffset16()),
        ClassDef: scope.ParseCommonTable<ClassDef>(scope.Reader.ReadOffset16()),
        RuleSets: ClassSequenceRuleSet.ParseListFromOffsets16(scope));

    public override bool TryMatch(ushort[] glyphs, int startIndex, out SequenceMatch match)
    {
        match = default;
        return Coverage.TryGetIndex(glyphs[startIndex], out _) && ClassDef.TryGetClass(glyphs[startIndex], out var cls) && RuleSets[cls].TryMatch(ClassDef, glyphs, startIndex, out match);
    }
}
