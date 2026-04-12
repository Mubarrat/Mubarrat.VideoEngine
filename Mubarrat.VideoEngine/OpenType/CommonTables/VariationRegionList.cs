namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct VariationRegionList(ushort AxisCount, VariationRegion[] Regions) : IOpenTypeCommonTable<VariationRegionList>
{
    public static VariationRegionList Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        ushort axisCount = scope.Reader.ReadUInt16();
        var regions = new VariationRegion[scope.Reader.ReadUInt16()];
        for (int i = 0; i < regions.Length; i++)
            regions[i] = VariationRegion.Parse(scope, axisCount);
        return new VariationRegionList(axisCount, regions);
    }
}
