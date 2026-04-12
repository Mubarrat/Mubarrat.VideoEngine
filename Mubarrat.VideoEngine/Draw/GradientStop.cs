using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Draw;

[StructLayout(LayoutKind.Explicit)]
public struct GradientStop : IComparable<GradientStop>, IEquatable<GradientStop>
{
    [FieldOffset(0)] public double Offset;
    [FieldOffset(8)] public Color32 Color;
    [FieldOffset(8)] public byte B;
    [FieldOffset(9)] public byte G;
    [FieldOffset(10)] public byte R;
    [FieldOffset(11)] public byte A;
    [FieldOffset(8)] public uint Value;

    public GradientStop(double offset, Color32 color)
    {
        Offset = offset;
        Color = color;
    }

    public GradientStop(double offset, byte r, byte g, byte b, byte a = 255)
    {
        Offset = offset;
        R = r; G = g; B = b; A = a;
    }

    public GradientStop(double offset, uint value)
    {
        Offset = offset;
        Value = value;
    }

    public readonly int CompareTo(GradientStop other) => Offset.CompareTo(other.Offset);

    public static bool operator <(GradientStop left, GradientStop right) => left.Offset < right.Offset;

    public static bool operator <=(GradientStop left, GradientStop right) => left.Offset <= right.Offset;

    public static bool operator >(GradientStop left, GradientStop right) => left.Offset > right.Offset;

    public static bool operator >=(GradientStop left, GradientStop right) => left.Offset >= right.Offset;

    public readonly bool Equals(GradientStop other) => Offset == other.Offset && Value == other.Value;

    public override readonly bool Equals(object? obj) => obj is GradientStop other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(Offset, Value);

    public static bool operator ==(GradientStop left, GradientStop right) => left.Equals(right);

    public static bool operator !=(GradientStop left, GradientStop right) => !(left == right);
}
