namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct SequenceRuleSet(SequenceRule[] Rules) : IOpenTypeCommonTable<SequenceRuleSet>
{
    public static SequenceRuleSet Parse(OpenTypeReader.TableScope scope, object? param = null) => new(SequenceRule.ParseListFromOffsets16(scope, param));

    public bool TryMatch(ushort[] glyphs, int startIndex, out SequenceMatch match)
    {
        foreach (var rule in Rules)
            if (rule.TryMatch(glyphs, startIndex, out match))
                return true;

        match = default;
        return false;
    }
}
