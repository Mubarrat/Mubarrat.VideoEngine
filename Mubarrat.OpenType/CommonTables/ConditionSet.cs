namespace Mubarrat.OpenType.CommonTables;

public readonly record struct ConditionSet(ConditionTable[] Conditions) : IOpenTypeCommonTable<ConditionSet>
{
    public bool Matches(FontVariationContext context) => Conditions.All(x => x.Matches(context));

    public static ConditionSet Parse(OpenTypeReader.TableScope scope, object? param = null) => new(ConditionTable.ParseListFromOffsets32(scope));
}
