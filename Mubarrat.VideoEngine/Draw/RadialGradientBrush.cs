using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Draw;

[StructLayout(LayoutKind.Explicit)]
public struct RadialGradientBrush : IBrush, IEquatable<RadialGradientBrush>
{
    [FieldOffset(0)] public Point Center;
    [FieldOffset(0)] public double CenterX;
    [FieldOffset(8)] public double CenterY;
    [FieldOffset(16)] public double Radius;
    [FieldOffset(24)] public GradientStop[] Stops;

    public RadialGradientBrush(double centerX, double centerY, double radius, params IEnumerable<GradientStop> stops)
    {
        CenterX = centerX;
        CenterY = centerY;
        Radius = radius;
        Stops = [.. stops];
        Array.Sort(Stops);
    }

    public RadialGradientBrush(Point center, double radius, params IEnumerable<GradientStop> stops)
    {
        Center = center;
        Radius = radius;
        Stops = [.. stops];
        Array.Sort(Stops);
    }

    public Color32 Sample(double x, double y) => Stops.Length == 0 ? default
        : Stops.FindColor(Math.Clamp(Math.Sqrt(new Vector2D(x - CenterX, y - CenterY).LengthSquared * 2) / Radius, 0, 1));

    public readonly bool Equals(RadialGradientBrush other) => Center == other.Center && Radius == other.Radius && Stops.SequenceEqual(other.Stops);

    public override readonly bool Equals(object? obj) => obj is RadialGradientBrush other && Equals(other);

    public static bool operator ==(RadialGradientBrush left, RadialGradientBrush right) => left.Equals(right);

    public static bool operator !=(RadialGradientBrush left, RadialGradientBrush right) => !(left == right);

    public override readonly int GetHashCode() => HashCode.Combine(Center, Radius, Stops);
}
