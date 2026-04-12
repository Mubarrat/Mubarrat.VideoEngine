namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct LangSys(ushort LookupOrderOffset, ushort RequestedFeatureIndex, ushort[] FeatureIndices) : IOpenTypeCommonTable<LangSys>
{
    public static LangSys Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
        LookupOrderOffset: scope.Reader.ReadOffset16(), // can't dereference this, but spec requires it to be 0
        RequestedFeatureIndex: scope.Reader.ReadUInt16(),
        FeatureIndices: scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()));
}
