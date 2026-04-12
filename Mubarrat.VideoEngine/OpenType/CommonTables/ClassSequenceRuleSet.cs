namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct ClassSequenceRuleSet(ClassSequenceRule[] Rules) : IOpenTypeCommonTable<ClassSequenceRuleSet>
{
    public static ClassSequenceRuleSet Parse(OpenTypeReader.TableScope scope, object? param = null) => new(ClassSequenceRule.ParseListFromOffsets16(scope, param));

    public bool TryMatch(ClassDef classDef, ushort[] glyphs, int startIndex, out SequenceMatch match)
    {
        foreach (var rule in Rules)
        {
            if (rule.TryMatch(classDef, glyphs, startIndex, out match))
                return true;
        }

        match = default;
        return false;
    }
}
