namespace Mubarrat.OpenType.CommonTables;

public readonly record struct FeatureRecord(string Tag, Feature Feature)
    : IOpenTypeCommonTable<FeatureRecord>
{
    public static FeatureRecord Parse(OpenTypeReader.TableScope scopeOfList, object? param = null)
    {
        string tag = scopeOfList.Reader.ReadTag();
        return new(
            Tag: tag,
            Feature: scopeOfList.ParseCommonTable<Feature>(scopeOfList.Reader.ReadOffset16(), tag));
    }
}
