using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Draw;

[StructLayout(LayoutKind.Explicit)]
public struct ConicGradientBrush : IBrush, IEquatable<ConicGradientBrush>
{
    [FieldOffset(0)] public Point Center;
    [FieldOffset(0)] public double CenterX;
    [FieldOffset(8)] public double CenterY;
    [FieldOffset(16)] public double StartAngle;
    [FieldOffset(24)] public GradientStop[] Stops;

    public ConicGradientBrush(double centerX, double centerY, double startAngle, params IEnumerable<GradientStop> stops)
    {
        CenterX = centerX;
        CenterY = centerY;
        StartAngle = startAngle;
        Stops = [.. stops];
        Array.Sort(Stops);
    }

    public ConicGradientBrush(Point center, double startAngle, params IEnumerable<GradientStop> stops)
    {
        Center = center;
        StartAngle = startAngle;
        Stops = [.. stops];
        Array.Sort(Stops);
    }

    public readonly Color32 Sample(double x, double y)
    {
        if (Stops.Length == 0) return new(0);
        double t = (Math.Atan2(y - CenterY, x - CenterX) - StartAngle) / Math.Tau;
        t -= Math.Floor(t);
        return Stops.FindColor(t);
    }

    public readonly bool Equals(ConicGradientBrush other) => Center == other.Center && StartAngle == other.StartAngle && Stops.SequenceEqual(other.Stops);

    public override readonly bool Equals(object? obj) => obj is ConicGradientBrush other && Equals(other);

    public static bool operator ==(ConicGradientBrush left, ConicGradientBrush right) => left.Equals(right);

    public static bool operator !=(ConicGradientBrush left, ConicGradientBrush right) => !(left == right);

    public override readonly int GetHashCode() => HashCode.Combine(Center, StartAngle, Stops);
}
