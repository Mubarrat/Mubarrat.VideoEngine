using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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
    extension(Point[] @this)
    {
        public static Point[] operator *(Point[] points, Matrix2D m)
        {
            int n = points.Length;
            var result = new Point[n];
            int i = 0;
            if (Vector.IsHardwareAccelerated)
            {
                Vector<double> m11 = new(m.ScaleX), m12 = new(m.SkewX), m21 = new(m.SkewY), m22 = new(m.ScaleY), m13 = new(m.OffsetX), m23 = new(m.OffsetY);
                int simdCount = Vector<double>.Count;
                Span<double> xs = stackalloc double[simdCount], ys = stackalloc double[simdCount];
                for (; i + simdCount <= n; i += simdCount)
                {
                    for (int j = 0; j < simdCount; j++)
                    {
                        xs[j] = points[i + j].X;
                        ys[j] = points[i + j].Y;
                    }
                    Vector<double> vx = new(xs), vy = new(ys), rx = vx * m11 + vy * m21 + m13, ry = vx * m12 + vy * m22 + m23;
                    for (int j = 0; j < simdCount; j++)
                        result[i + j] = new Point(rx[j], ry[j]);
                }
            }
            for (; i < n; i++)
                result[i] = points[i] * m;
            return result;
        }

        public static Point[] operator /(Point[] points, Matrix2D m) => points * m.Inverse;

        public void operator *=(Matrix2D m)
        {
            int n = @this.Length;
            int i = 0;
            if (Vector.IsHardwareAccelerated)
            {
                Vector<double> m11 = new(m.ScaleX), m12 = new(m.SkewX), m21 = new(m.SkewY), m22 = new(m.ScaleY), m13 = new(m.OffsetX), m23 = new(m.OffsetY);
                int simdCount = Vector<double>.Count;
                Span<double> xs = stackalloc double[simdCount], ys = stackalloc double[simdCount];
                for (; i + simdCount <= n; i += simdCount)
                {
                    for (int j = 0; j < simdCount; j++)
                    {
                        xs[j] = @this[i + j].X;
                        ys[j] = @this[i + j].Y;
                    }
                    Vector<double> vx = new(xs), vy = new(ys), rx = vx * m11 + vy * m21 + m13, ry = vx * m12 + vy * m22 + m23;
                    for (int j = 0; j < simdCount; j++)
                        @this[i + j] = new Point(rx[j], ry[j]);
                }
            }
            for (; i < n; i++)
                @this[i] *= m;
        }
    }
}
