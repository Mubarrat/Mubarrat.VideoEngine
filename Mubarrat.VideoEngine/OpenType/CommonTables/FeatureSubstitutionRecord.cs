namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct FeatureSubstitutionRecord(ushort FeatureIndex, Feature AlternateFeature)
    : IOpenTypeCommonTable<FeatureSubstitutionRecord>
{
    public static FeatureSubstitutionRecord Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
        FeatureIndex: scope.Reader.ReadUInt16(),
        AlternateFeature: scope.ParseCommonTable<Feature>(scope.Reader.ReadOffset32()));
}
