namespace Mubarrat.OpenType.CommonTables;

public readonly record struct ItemVariationStore(ushort Format, VariationRegionList VariationRegionList, ItemVariationData[] ItemVariationDatas) : IOpenTypeCommonTable<ItemVariationStore>
{
    public static ItemVariationStore Parse(OpenTypeReader.TableScope scope, object? param = null) => new(scope.Reader.ReadUInt16(), scope.ParseCommonTable<VariationRegionList>(scope.Reader.ReadOffset32()), ItemVariationData.ParseListFromOffsets16(scope));

    public float Resolve(VariationIndexTable variationIndex, VariationInstance instance)
    {
        if (variationIndex.DeltaSetOuterIndex == ushort.MaxValue && variationIndex.DeltaSetInnerIndex == ushort.MaxValue)
            return 0f;

        int outerIndex = variationIndex.DeltaSetOuterIndex;
        if ((uint)outerIndex >= (uint)ItemVariationDatas.Length)
            return 0f;

        var data = ItemVariationDatas[outerIndex];
        return data.Resolve(variationIndex.DeltaSetInnerIndex, VariationRegionList, instance);
    }
}
