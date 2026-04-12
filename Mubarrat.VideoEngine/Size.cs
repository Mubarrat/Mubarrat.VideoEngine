using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine;

[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 16)]
public struct Size : IEquatable<Size>, ILerpable<Size>
{
    public double Width, Height;

    public Size(double width, double height) => (Width, Height) = (width, height);
    public Size(double uniform) : this(uniform, uniform) { }

    public static Size Zero => new(0);
    public static Size Unit => new(1);
    public static Size Minimum => new(double.NegativeInfinity);
    public static Size Maximum => new(double.PositiveInfinity);
    public static Size NaN => new(double.NaN);

    public readonly double Area => Width * Height;
    public readonly double MaxDimension => Math.Max(Width, Height);
    public readonly double MinDimension => Math.Min(Width, Height);

    public readonly Size Lerp(in Size target, double t) => new(Width.Lerp(target.Width, t), Height.Lerp(target.Height, t));

    public readonly Size Clamp(Size min, Size max) => new(Math.Clamp(Width, min.Width, max.Width), Math.Clamp(Height, min.Height, max.Height));
    public static Size Min(Size a, Size b) => new(Math.Min(a.Width, b.Width), Math.Min(a.Height, b.Height));
    public static Size Max(Size a, Size b) => new(Math.Max(a.Width, b.Width), Math.Max(a.Height, b.Height));

    public readonly Size Abs => new(Math.Abs(Width), Math.Abs(Height));

    public static Size operator +(Size a, Size b) => new(a.Width + b.Width, a.Height + b.Height);
    public static Size operator -(Size a, Size b) => new(a.Width - b.Width, a.Height - b.Height);
    public static Size operator *(Size s, double scale) => new(s.Width * scale, s.Height * scale);
    public static Size operator /(Size s, double scale) => new(s.Width / scale, s.Height / scale);

    public static Size operator *(Size s, Matrix2D m) => new(m.ScaleX * s.Width + m.SkewY * s.Height, m.SkewX * s.Width + m.ScaleY * s.Height);

    public static implicit operator Size((double, double) t) => new(t.Item1, t.Item2);

    public static explicit operator Vector2D(Size s) => new(s.Width, s.Height);
    public static explicit operator Point(Size s) => new(s.Width, s.Height);

    public static explicit operator Size(Vector2D v) => new(v.X, v.Y);
    public static explicit operator Size(Point p) => new(p.X, p.Y);

    public readonly void Deconstruct(out double width, out double height) => (width, height) = (Width, Height);

    public readonly bool Equals(Size other) => Width == other.Width && Height == other.Height;

    public override readonly bool Equals(object? obj) => obj is Size p && Equals(p);

    public static bool operator ==(Size left, Size right) => left.Equals(right);

    public static bool operator !=(Size left, Size right) => !(left == right);

    public override readonly int GetHashCode() => HashCode.Combine(Width, Height);

    public override readonly string ToString() => $"[{Width}, {Height}]";
}
