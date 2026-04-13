namespace Mubarrat.OpenType.CommonTables;

public readonly record struct FeatureVariations(FeatureVariationRecord[] Records) : IOpenTypeCommonTable<FeatureVariations>
{
    public static FeatureVariations Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        scope.Reader.ReadUInt16(); // major
        scope.Reader.ReadUInt16(); // minor
        return new(FeatureVariationRecord.ParseListFromOffsets32(scope, scope.Reader.ReadUInt32()));
    }

    public FeatureTableSubstitution? Resolve(FontVariationContext context)
    {
        foreach (var r in Records)
            if (r.Matches(context))
                return r.Substitution;
        return null;
    }
}
