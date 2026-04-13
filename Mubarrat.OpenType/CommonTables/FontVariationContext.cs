namespace Mubarrat.OpenType.CommonTables;

public readonly struct FontVariationContext(float[] axes)
{
    private readonly float[] axes = axes;

    public float GetAxis(int index) => (uint)index < (uint)axes.Length ? axes[index] : 0;
}
