namespace Mubarrat.OpenType.CommonTables;

public readonly record struct VariationRegion(RegionAxisCoordinates[] Axes)
{
    public static VariationRegion Parse(OpenTypeReader.TableScope scope, ushort axisCount) => new(RegionAxisCoordinates.ParseListContiguous(scope, axisCount));

    public float GetScalar(VariationInstance instance)
    {
        float scalar = 1f;

        for (int i = 0; i < Axes.Length; i++)
        {
            float axisScalar = Axes[i].GetScalar(instance.GetAxis((ushort)i));
            if (axisScalar == 0f)
                return 0f;

            scalar *= axisScalar;
        }

        return scalar;
    }
}
