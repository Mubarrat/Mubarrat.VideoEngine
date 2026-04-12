namespace Mubarrat.VideoEngine.OpenType.CommonTables;

public readonly record struct VariationInstance(float[] NormalizedCoordinates)
{
    public float GetAxis(ushort axisIndex)
        => axisIndex < (uint)NormalizedCoordinates.Length ? NormalizedCoordinates[axisIndex] : 0f;
}
