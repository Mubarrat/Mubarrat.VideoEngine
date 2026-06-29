namespace Mubarrat.VideoEngine.Fields2D;

public readonly struct FieldInterval(double min, double max)
{
    public readonly double Min = min, Max = max;

    public static FieldInterval FromValue(double v) => new(v, v);

    public static FieldInterval Unknown() => new(double.NegativeInfinity, double.PositiveInfinity);

    public static FieldInterval Union(FieldInterval a, FieldInterval b) => new(Math.Min(a.Min, b.Min), Math.Max(a.Max, b.Max));

    public bool IsFullyAbove(double threshold) => Min > threshold;

    public bool IsFullyBelow(double threshold) => Max < threshold;
}
