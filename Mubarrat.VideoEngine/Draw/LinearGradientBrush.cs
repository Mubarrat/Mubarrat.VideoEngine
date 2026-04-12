using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Draw;

[StructLayout(LayoutKind.Sequential)]
public struct LinearGradientBrush : IBrush, IEquatable<LinearGradientBrush>
{
    public double Sin, Cos;
    public GradientStop[] Stops;

    public LinearGradientBrush(double angle, params IEnumerable<GradientStop> stops)
    {
        (Sin, Cos) = Math.SinCos(angle);
        Stops = [.. stops];
        Array.Sort(Stops);
    }

    public LinearGradientBrush(double sin, double cos, params IEnumerable<GradientStop> stops)
    {
        Sin = sin; Cos = cos;
        Stops = [.. stops];
        Array.Sort(Stops);
    }

    public readonly Color32 Sample(double x, double y) => Stops.Length == 0 ? default
        : Stops.FindColor(Math.FusedMultiplyAdd(x - 0.5, Cos, Math.FusedMultiplyAdd(y - 0.5, Sin, 0.5)));

    public readonly bool Equals(LinearGradientBrush other) => Sin == other.Sin && Cos == other.Cos && Stops.SequenceEqual(other.Stops);

    public override readonly bool Equals(object? obj) => obj is LinearGradientBrush other && Equals(other);

    public static bool operator ==(LinearGradientBrush left, LinearGradientBrush right) => left.Equals(right);

    public static bool operator !=(LinearGradientBrush left, LinearGradientBrush right) => !(left == right);

    public override readonly int GetHashCode() => HashCode.Combine(Sin, Cos, Stops);
}
