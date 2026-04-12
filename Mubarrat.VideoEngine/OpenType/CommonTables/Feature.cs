namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct Feature(FeatureParam? Param, ushort[] LookupListIndices) : IOpenTypeCommonTable<Feature>
{
    public static Feature Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
        Param: scope.ParseCommonTableOrDefault<FeatureParam>(scope.Reader.ReadOffset16(), param),
        LookupListIndices: scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()));
}
