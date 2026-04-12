using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Draw;

[StructLayout(LayoutKind.Explicit)]
public struct SolidColorBrush : IBrush, ILerpable<SolidColorBrush>, IEquatable<SolidColorBrush>
{
    [FieldOffset(0)] public Color32 Color;
    [FieldOffset(0)] public byte B;
    [FieldOffset(1)] public byte G;
    [FieldOffset(2)] public byte R;
    [FieldOffset(3)] public byte A;
    [FieldOffset(0)] public uint Value;

    public SolidColorBrush(Color32 color) => Color = color;

    public SolidColorBrush(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }

    public SolidColorBrush(uint value) => Value = value;

    public readonly SolidColorBrush Lerp(in SolidColorBrush other, double t) => new(Color.Lerp(other.Color, t));

    public readonly Color32 Sample(double x, double y) => Color;

    public readonly bool Equals(SolidColorBrush other) => Value == other.Value;

    public static explicit operator Color32(SolidColorBrush brush) => brush.Color;
    public static explicit operator SolidColorBrush(Color32 color) => new(color);

    public override readonly bool Equals(object? obj) => obj is SolidColorBrush other && Equals(other);

    public static bool operator ==(SolidColorBrush left, SolidColorBrush right) => left.Equals(right);

    public static bool operator !=(SolidColorBrush left, SolidColorBrush right) => !(left == right);

    public readonly void Deconstruct(out byte r, out byte g, out byte b, out byte a) { r = R; g = G; b = B; a = A; }
    public readonly void Deconstruct(out byte r, out byte g, out byte b) { r = R; g = G; b = B; }

    public override readonly int GetHashCode() => Value.GetHashCode();
}
