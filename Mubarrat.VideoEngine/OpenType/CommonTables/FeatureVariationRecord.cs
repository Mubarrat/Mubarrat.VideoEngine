namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct FeatureVariationRecord(ConditionSet? ConditionSet, FeatureTableSubstitution? Substitution) : IOpenTypeCommonTable<FeatureVariationRecord>
{
    public bool Matches(FontVariationContext context) => ConditionSet?.Matches(context) ?? true;

    public static FeatureVariationRecord Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
        scope.ParseCommonTableOrDefault<ConditionSet>(scope.Reader.ReadOffset32()),
        scope.ParseCommonTableOrDefault<FeatureTableSubstitution>(scope.Reader.ReadOffset32()));
}
