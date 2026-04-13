namespace Mubarrat.OpenType.CommonTables;

public readonly record struct ItemVariationData(ushort ItemCount, ushort WordDeltaCountRaw, ushort[] RegionIndexes, DeltaSet[] DeltaSets) : IOpenTypeCommonTable<ItemVariationData>
{
    public bool LongWords => (WordDeltaCountRaw & 0x8000) != 0;
    public ushort WordDeltaCount => (ushort)(WordDeltaCountRaw & 0x7FFF);

    public static ItemVariationData Parse(OpenTypeReader.TableScope scope, object? param = null)
    {
        ushort itemCount = scope.Reader.ReadUInt16();
        ushort wordDeltaCountRaw = scope.Reader.ReadUInt16();
        ushort regionIndexCount = scope.Reader.ReadUInt16();

        var regionIndexes = scope.Reader.ReadUInt16Array(regionIndexCount);

        var deltaSets = new DeltaSet[itemCount];

        bool longWords = (wordDeltaCountRaw & 0x8000) != 0;
        ushort wordCount = (ushort)(wordDeltaCountRaw & 0x7FFF);

        for (int i = 0; i < itemCount; i++)
            deltaSets[i] = DeltaSet.Parse(scope, regionIndexCount, wordCount, longWords);

        return new ItemVariationData(itemCount, wordDeltaCountRaw, regionIndexes, deltaSets);
    }

    public float Resolve(ushort innerIndex, VariationRegionList regionList, VariationInstance instance)
    {
        if ((uint)innerIndex >= (uint)DeltaSets.Length)
            return 0f;

        var row = DeltaSets[innerIndex];
        float delta = 0f;

        for (int i = 0; i < RegionIndexes.Length; i++)
        {
            int regionIndex = RegionIndexes[i];
            if ((uint)regionIndex >= (uint)regionList.Regions.Length)
                continue;

            float scalar = regionList.Regions[regionIndex].GetScalar(instance);
            if (scalar == 0f)
                continue;

            delta += row.Deltas[i] * scalar;
        }

        return delta;
    }
}
