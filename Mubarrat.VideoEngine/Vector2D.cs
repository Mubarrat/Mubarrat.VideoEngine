using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Mubarrat.VideoEngine;

[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 16)]
public struct Vector2D : IEquatable<Vector2D>, ILerpable<Vector2D>
{
    public double X, Y;

    public static Vector2D Zero => new(0);
    public static Vector2D UnitX => new(1, 0);
    public static Vector2D UnitY => new(0, 1);
    public static Vector2D Unit => new(1);
    public static Vector2D Minimum => new(double.NegativeInfinity);
    public static Vector2D Maximum => new(double.PositiveInfinity);
    public static Vector2D NaN => new(double.NaN);

    public Vector2D(double x, double y) => (X, Y) = (x, y);
    public Vector2D(double uniform) : this(uniform, uniform) { }

    public readonly double Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Math.Sqrt(LengthSquared);
    }

    public readonly double LengthSquared
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Math.FusedMultiplyAdd(X, X, Y * Y); // better than X * X + Y * Y in terms of precision and performance
    }

    public readonly Vector2D Normalize() => Length is var len and > 1e-10 ? new Vector2D(X / len, Y / len) : Zero;

    public readonly double Dot(Vector2D other) => X * other.X + Y * other.Y;
    public readonly double Cross(Vector2D other) => X * other.Y - Y * other.X;

    public readonly Vector2D ProjectOnto(Vector2D other)
    {
        var denom = other.LengthSquared;
        return denom > 1e-10 ? other * (Dot(other) / denom) : Zero;
    }

    public readonly Vector2D Reflect(Vector2D normal)
    {
        var n = normal.Normalize();
        return this - 2 * Dot(n) * n;
    }

    public readonly double AngleTo(Vector2D other) => Math.Atan2(Cross(other), Dot(other));

    public readonly Vector2D Rotate(double radians)
    {
        var (sin, cos) = Math.SinCos(radians);
        return new Vector2D(cos * X - sin * Y, sin * X + cos * Y);
    }

    public readonly Vector2D Lerp(in Vector2D target, double t) => new(X + (target.X - X) * t, Y + (target.Y - Y) * t);

    public readonly Point ToPoint() => new(X, Y);
    public readonly Vector2D Abs() => new(Math.Abs(X), Math.Abs(Y));
    public readonly Vector2D Round() => new(Math.Round(X), Math.Round(Y));

    public readonly Vector2D Clamp(Vector2D min, Vector2D max) => new(Math.Clamp(X, min.X, max.X), Math.Clamp(Y, min.Y, max.Y));
    public static Vector2D Min(Vector2D a, Vector2D b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
    public static Vector2D Max(Vector2D a, Vector2D b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public static Vector2D operator +(Vector2D a, Vector2D b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2D operator -(Vector2D a, Vector2D b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2D operator -(Vector2D v) => new(-v.X, -v.Y);
    public static Vector2D operator *(Vector2D v, double s) => new(v.X * s, v.Y * s);
    public static Vector2D operator *(double s, Vector2D v) => new(v.X * s, v.Y * s);
    public static Vector2D operator /(Vector2D v, double s) => new(v.X / s, v.Y / s);
    public static Vector2D operator *(Vector2D v, Matrix2D m) => new(v.X * m.ScaleX + v.Y * m.SkewX + m.OffsetX, v.X * m.SkewY + v.Y * m.ScaleY + m.OffsetY);

    public static implicit operator Vector2D((double, double) t) => new(t.Item1, t.Item2);

    public readonly void Deconstruct(out double x, out double y) => (x, y) = (X, Y);

    public readonly bool Equals(Vector2D other) => X == other.X && Y == other.Y;

    public override readonly bool Equals(object? obj) => obj is Vector2D v && Equals(v);

    public static bool operator ==(Vector2D left, Vector2D right) => left.Equals(right);

    public static bool operator !=(Vector2D left, Vector2D right) => !(left == right);

    public override readonly int GetHashCode() => HashCode.Combine(X, Y);

    public override readonly string ToString() => $"<{X}, {Y}>";
}

public static class Vector2DExtensions
{
    extension(ref Span<Vector2D> vectors)
    {
        public static unsafe Vector2D[] operator *(Span<Vector2D> _vectors, Matrix2D m)
        {
            var result = new Vector2D[_vectors.Length];
            int length = _vectors.Length * 2, i = 0;
            if (Avx.IsSupported)
                fixed (double* d0 = &_vectors[0].X, r0 = &result[0].X)
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
            for (int p = i >> 1; p < _vectors.Length; p++)
                result[p] = _vectors[p] * m;
            return result;
        }

        public static Vector2D[] operator /(Span<Vector2D> _vectors, Matrix2D m) => _vectors * m.Inverse;

        public unsafe void operator *=(Matrix2D m)
        {
            int length = vectors.Length * 2, i = 0;
            if (Avx.IsSupported)
                fixed (double* d0 = &vectors[0].X)
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
            for (int p = i >> 1; p < vectors.Length; p++)
                vectors[p] *= m;
        }

        public void operator /=(Matrix2D m) => vectors *= m.Inverse;
    }
}
