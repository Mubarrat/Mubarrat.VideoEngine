using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Draw;

[StructLayout(LayoutKind.Sequential)]
public struct LerpBrush(IBrush from, IBrush to, double time) : IBrush, IEquatable<LerpBrush>
{
    public IBrush From = from, To = to;
    public double Time = time;

    public readonly Color32 Sample(double x, double y) => From.Sample(x, y).Lerp(To.Sample(x, y), Time);

    public readonly bool Equals(LerpBrush other) => From.Equals(other.From) && To.Equals(other.To) && Time == other.Time;

    public override readonly bool Equals(object? obj) => obj is LerpBrush other && Equals(other);

    public static bool operator ==(LerpBrush left, LerpBrush right) => left.Equals(right);

    public static bool operator !=(LerpBrush left, LerpBrush right) => !(left == right);

    public override readonly int GetHashCode() => HashCode.Combine(From, To, Time);
}
