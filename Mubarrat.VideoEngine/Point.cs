using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mubarrat.VideoEngine;

[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 16)]
public struct Point(double x, double y) : IEquatable<Point>, ILerpable<Point>
{
    public double X = x, Y = y;

    public Point(double uniform) : this(uniform, uniform) { }

    public static Point Zero => new(0);
    public static Point UnitX => new(1, 0);
    public static Point UnitY => new(0, 1);
    public static Point Unit => new(1);
    public static Point Minimum => new(double.NegativeInfinity);
    public static Point Maximum => new(double.PositiveInfinity);
    public static Point NaN => new(double.NaN);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double DistanceTo(Point other) => Math.Sqrt(DistanceSquaredTo(other));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly double DistanceSquaredTo(Point other)
    {
        double dx = other.X - X, dy = other.Y - Y;
        return Math.FusedMultiplyAdd(dx, dx, dy * dy);
    }

    public readonly Point Lerp(in Point target, double t) => new(X.Lerp(target.X, t), Y.Lerp(target.Y, t));

    public readonly Point Clamp(Point min, Point max) => new(Math.Clamp(X, min.X, max.X), Math.Clamp(Y, min.Y, max.Y));
    public static Point Min(Point a, Point b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
    public static Point Max(Point a, Point b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public readonly Point Swap() => new(Y, X);
    public readonly Point Abs() => new(Math.Abs(X), Math.Abs(Y));

    public readonly Point Skew(double sx, double sy) => new(X + Y * sx, X * sy + Y);
    public readonly Point Rotate(double radians)
    {
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        return new(X * cos - Y * sin, X * sin + Y * cos);
    }

    public static Vector2D operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
    public static Point operator +(Point p, Vector2D v) => new(p.X + v.X, p.Y + v.Y);
    public void operator +=(Vector2D v) { X += v.X; Y += v.Y; }
    public static Point operator +(Vector2D v, Point p) => new(p.X + v.X, p.Y + v.Y);
    public static Point operator -(Point p, Vector2D v) => new(p.X - v.X, p.Y - v.Y);
    public void operator -=(Vector2D v) { X -= v.X; Y -= v.Y; }
    public static Point operator +(Point p) => p;
    public static Point operator -(Point p) => new(-p.X, -p.Y);

    public static Point operator *(Point p, double s) => new(p.X * s, p.Y * s);
    public void operator *=(double s) { X *= s; Y *= s; }
    public static Point operator *(Point p, Matrix2D m) => new(m.ScaleX * p.X + m.SkewY * p.Y + m.OffsetX, m.SkewX * p.X + m.ScaleY * p.Y + m.OffsetY);
    public void operator *=(Matrix2D m) => (X, Y) = (m.ScaleX * X + m.SkewY * Y + m.OffsetX, m.SkewX * X + m.ScaleY * Y + m.OffsetY);
    public static Point operator /(Point p, double s) => new(p.X / s, p.Y / s);
    public void operator /=(double s) { X /= s; Y /= s; }
    public static Point operator /(Point p, Matrix2D m) => p * m.Inverse;
    public void operator /=(Matrix2D m) => this *= m.Inverse;

    public static implicit operator Point((double, double) t) => new(t.Item1, t.Item2);

    public static explicit operator Vector2D(Point p) => new(p.X, p.Y);
    public static explicit operator Point(Vector2D v) => new(v.X, v.Y);
    
    public readonly void Deconstruct(out double x, out double y) { x = X; y = Y; }

    public readonly bool Equals(Point other) => X == other.X && Y == other.Y;

    public override readonly bool Equals(object? obj) => obj is Point p && Equals(p);

    public static bool operator ==(Point left, Point right) => left.Equals(right);

    public static bool operator !=(Point left, Point right) => !(left == right);

    public override readonly int GetHashCode() => HashCode.Combine(X, Y);

    public override readonly string ToString() => $"({X}, {Y})";
}

public static class PointExtensions
{
    extension(ref Span<Point> points)
    {
        public static unsafe Point[] operator *(Span<Point> _points, Matrix2D m)
        {
            var result = new Point[_points.Length];
            int length = _points.Length * 2, i = 0;
            if (Avx.IsSupported)
                fixed (double* d0 = &_points[0].X, r0 = &result[0].X)
                {
                    Vector256<double>
                        m11 = Vector256.Create(m.ScaleX), m12 = Vector256.Create(m.SkewX), m21 = Vector256.Create(m.SkewY),
                        m22 = Vector256.Create(m.ScaleY), m13 = Vector256.Create(m.OffsetX), m23 = Vector256.Create(m.OffsetY);
                    for (; i <= length - 4; i += 4)
                    {
                        Vector256<double> v = Avx.LoadVector256(d0 + i), vx = Avx.Permute(v, 0b11011000), vy = Avx.Permute(v, 0b11111101);
                        Avx.Store(r0 + i, Avx.UnpackLow(
                            Vector256.FusedMultiplyAdd(vx, m11, Vector256.FusedMultiplyAdd(vy, m21, m13)),
                            Vector256.FusedMultiplyAdd(vx, m12, Vector256.FusedMultiplyAdd(vy, m22, m23))));
                    }
                }
            for (int p = i >> 1; p < _points.Length; p++)
                result[p] = _points[p] * m;
            return result;
        }

        public static Point[] operator /(Span<Point> _points, Matrix2D m) => _points * m.Inverse;

        public unsafe void operator *=(Matrix2D m)
        {
            int length = points.Length * 2, i = 0;
            if (Avx.IsSupported)
                fixed (double* d0 = &points[0].X)
                {
                    Vector256<double>
                        m11 = Vector256.Create(m.ScaleX), m12 = Vector256.Create(m.SkewX), m21 = Vector256.Create(m.SkewY),
                        m22 = Vector256.Create(m.ScaleY), m13 = Vector256.Create(m.OffsetX), m23 = Vector256.Create(m.OffsetY);
                    for (; i <= length - 4; i += 4)
                    {
                        Vector256<double> v = Avx.LoadVector256(d0 + i), vx = Avx.Permute(v, 0b11011000), vy = Avx.Permute(v, 0b11111101);
                        Avx.Store(d0 + i, Avx.UnpackLow(
                            Vector256.FusedMultiplyAdd(vx, m11, Vector256.FusedMultiplyAdd(vy, m21, m13)),
                            Vector256.FusedMultiplyAdd(vx, m12, Vector256.FusedMultiplyAdd(vy, m22, m23))));
                    }
                }
            for (int p = i >> 1; p < points.Length; p++)
                points[p] *= m;
        }

        public void operator /=(Matrix2D m) => points *= m.Inverse;
    }
}
