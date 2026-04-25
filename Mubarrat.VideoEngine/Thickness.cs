using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine;

[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 32)]
public struct Thickness : IEquatable<Thickness>, ILerpable<Thickness>
{
    public double Left, Top, Right, Bottom;

    public static readonly Thickness Zero = new(0);
    public static readonly Thickness Unit = new(1);

    public Thickness(double uniform) : this(uniform, uniform) { }

    public Thickness(double x, double y) : this(x, y, x, y) { }

    public Thickness(double left, double top, double right, double bottom)
        => (Left, Top, Right, Bottom) = (left, top, right, bottom);

    public readonly double Horizontal => Left + Right;

    public readonly double Vertical => Top + Bottom;

    public readonly Size MinimumSize => new(Horizontal, Vertical);

    public readonly bool IsZero => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;

    public readonly bool IsUniform => Left == Top && Top == Right && Right == Bottom;

    public readonly Thickness ClampNonNegative()
        => new(Math.Max(0, Left), Math.Max(0, Top), Math.Max(0, Right), Math.Max(0, Bottom));

    public readonly Thickness Lerp(in Thickness target, double t)
        => new(Left.Lerp(target.Left, t), Top.Lerp(target.Top, t), Right.Lerp(target.Right, t), Bottom.Lerp(target.Bottom, t));

    public readonly void Deconstruct(out double left, out double top, out double right, out double bottom)
        => (left, top, right, bottom) = (Left, Top, Right, Bottom);

    public readonly bool Equals(Thickness other)
        => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;

    public override readonly bool Equals(object? obj) => obj is Thickness other && Equals(other);

    public static bool operator ==(Thickness left, Thickness right) => left.Equals(right);

    public static bool operator !=(Thickness left, Thickness right) => !left.Equals(right);

    public override readonly int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    public override readonly string ToString() => $"Thickness({Left}, {Top}, {Right}, {Bottom})";

    public static Thickness operator +(Thickness a, Thickness b) => new(a.Left + b.Left, a.Top + b.Top, a.Right + b.Right, a.Bottom + b.Bottom);
    public static Thickness operator -(Thickness a, Thickness b) => new(a.Left - b.Left, a.Top - b.Top, a.Right - b.Right, a.Bottom - b.Bottom);
    public static Thickness operator *(Thickness a, double scalar) => new(a.Left * scalar, a.Top * scalar, a.Right * scalar, a.Bottom * scalar);
    public static Thickness operator *(double scalar, Thickness a) => a * scalar;
    public static Thickness operator /(Thickness a, double scalar) => new(a.Left / scalar, a.Top / scalar, a.Right / scalar, a.Bottom / scalar);
    public static Thickness operator +(Thickness a) => a;
    public static Thickness operator -(Thickness a) => new(-a.Left, -a.Top, -a.Right, -a.Bottom);

    public static Rect operator +(Rect rect, Thickness thickness) => new(rect.Left + thickness.Left, rect.Top + thickness.Top, rect.Width - thickness.Horizontal, rect.Height - thickness.Vertical);
    public static Rect operator -(Rect rect, Thickness thickness) => new(rect.Left - thickness.Left, rect.Top - thickness.Top, rect.Width + thickness.Horizontal, rect.Height + thickness.Vertical);
}
