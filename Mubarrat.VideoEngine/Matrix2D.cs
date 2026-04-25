using System.Numerics;
using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine;

[StructLayout(LayoutKind.Explicit, Pack = 8, Size = 48)]
public struct Matrix2D : IEquatable<Matrix2D>, ILerpable<Matrix2D>
{
    [field: FieldOffset(0)] public double ScaleX = 1;
    [field: FieldOffset(8)] public double SkewX = 0;
    [field: FieldOffset(16)] public double SkewY = 0;
    [field: FieldOffset(24)] public double ScaleY = 1;
    [field: FieldOffset(32)] public double OffsetX = 0;
    [field: FieldOffset(40)] public double OffsetY = 0;

    [field: FieldOffset(32)] public Point Offset;
    [field: FieldOffset(32)] public Vector2D OffsetVector;

    public Matrix2D(
        double scaleX = 1, double skewX = 0,
        double skewY = 0, double scaleY = 1,
        double offsetX = 0, double offsetY = 0) { ScaleX = scaleX; SkewX = skewX; SkewY = skewY; ScaleY = scaleY; OffsetX = offsetX; OffsetY = offsetY; }

    public static Matrix2D Identity => new(1, 0, 0, 1, 0, 0);

    public readonly bool IsIdentity => ScaleX == 1 && SkewX == 0 && SkewY == 0 && ScaleY == 1 && OffsetX == 0 && OffsetY == 0;

    public readonly double Determinant => ScaleX * ScaleY - SkewX * SkewY;

    public readonly bool IsInvertible => Determinant != 0;

    public readonly Matrix2D Inverse => Determinant is var det and not 0 ? new(
        ScaleY / det, -SkewX / det,
        -SkewY / det, ScaleX / det,
        (SkewX * OffsetY - ScaleY * OffsetX) / det,
        (SkewY * OffsetX - ScaleX * OffsetY) / det) : throw new InvalidOperationException("Matrix is not invertible");

    public void MutableInverse()
    {
        double det = Determinant;
        if (det == 0) throw new InvalidOperationException("Matrix is not invertible");
        (ScaleX, ScaleY) = (ScaleY / det, ScaleX / det);
        SkewX /= -det;
        SkewY /= -det;
        (OffsetX, OffsetY) = (-SkewX * OffsetY - ScaleX * OffsetX, -SkewY * OffsetX - ScaleY * OffsetY);
    }

    public static Matrix2D Translate(double x, double y) => new(1, 0, 0, 1, x, y);
    public static Matrix2D Translate(Point offset) => new(1, 0, 0, 1, offset.X, offset.Y);

    public static Matrix2D Scale(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    public static Matrix2D Skew(double sx, double sy) => new(1, sx, sy, 1, 0, 0);

    public static Matrix2D Rotate(double radians)
    {
        var (sin, cos) = Math.SinCos(radians);
        return new(cos, -sin, sin, cos, 0, 0);
    }

    public static Matrix2D operator +(Matrix2D a, Matrix2D b) => new(
        a.ScaleX + b.ScaleX, a.SkewX + b.SkewX,
        a.SkewY + b.SkewY, a.ScaleY + b.ScaleY,
        a.OffsetX + b.OffsetX, a.OffsetY + b.OffsetY);

    public static Matrix2D operator -(Matrix2D a, Matrix2D b) => new(
        a.ScaleX - b.ScaleX, a.SkewX - b.SkewX,
        a.SkewY - b.SkewY, a.ScaleY - b.ScaleY,
        a.OffsetX - b.OffsetX, a.OffsetY - b.OffsetY);

    public static Matrix2D operator -(Matrix2D a) => new(
        -a.ScaleX, -a.SkewX, -a.SkewY, -a.ScaleY, -a.OffsetX, -a.OffsetY);

    public static Matrix2D operator *(Matrix2D a, Matrix2D b) => new(
        a.ScaleX * b.ScaleX + a.SkewX * b.SkewY,
        a.ScaleX * b.SkewX + a.SkewX * b.ScaleY,
        a.SkewY * b.ScaleX + a.ScaleY * b.SkewY,
        a.SkewY * b.SkewX + a.ScaleY * b.ScaleY,
        a.OffsetX * b.ScaleX + a.OffsetY * b.SkewY + b.OffsetX,
        a.OffsetX * b.SkewX + a.OffsetY * b.ScaleY + b.OffsetY
    );

    public void operator *=(Matrix2D other)
    {
        double sx = ScaleX, sy = ScaleY, kx = SkewX, ky = SkewY, ox = OffsetX, oy = OffsetY;

        ScaleX = sx * other.ScaleX + kx * other.SkewY;
        SkewX = sx * other.SkewX + kx * other.ScaleY;

        SkewY = ky * other.ScaleX + sy * other.SkewY;
        ScaleY = ky * other.SkewX + sy * other.ScaleY;

        OffsetX = ox * other.ScaleX + oy * other.SkewY + other.OffsetX;
        OffsetY = ox * other.SkewX + oy * other.ScaleY + other.OffsetY;
    }

    public static Matrix2D operator *(Matrix2D a, double b) => new(
        a.ScaleX * b, a.SkewX * b,
        a.SkewY * b, a.ScaleY * b,
        a.OffsetX * b, a.OffsetY * b);

    public void operator *=(double scalar)
    {
        ScaleX *= scalar;
        SkewX *= scalar;
        SkewY *= scalar;
        ScaleY *= scalar;
        OffsetX *= scalar;
        OffsetY *= scalar;
    }

    public static Matrix2D operator /(Matrix2D a, Matrix2D b) => a * b.Inverse;

    public void operator /=(Matrix2D other) => this *= other.Inverse;

    public static Matrix2D operator /(Matrix2D a, double b) => new(
        a.ScaleX / b, a.SkewX / b,
        a.SkewY / b, a.ScaleY / b,
        a.OffsetX / b, a.OffsetY / b);

    public void operator /=(double scalar)
    {
        ScaleX /= scalar;
        SkewX /= scalar;
        SkewY /= scalar;
        ScaleY /= scalar;
        OffsetX /= scalar;
        OffsetY /= scalar;
    }

    public void TranslateAppend(double x, double y)
    {
        OffsetX += x * ScaleX + y * SkewY;
        OffsetY += x * SkewX + y * ScaleY;
    }

    public void ScaleAppend(double sx, double sy)
    {
        ScaleX *= sx;
        SkewX *= sx;
        SkewY *= sy;
        ScaleY *= sy;
        OffsetX *= sx;
        OffsetY *= sy;
    }

    public void SkewAppend(double sx, double sy)
    {
        double scaleX = ScaleX, skewX = SkewX, offsetY = OffsetY, offsetX = OffsetX;
        ScaleX += sy * SkewX;
        SkewX += sy * ScaleY;
        SkewY += sx * scaleX;
        ScaleY += sx * skewX;
        OffsetX += sx * offsetY;
        OffsetY += sy * offsetX;
    }

    public void RotateAppend(double radians)
    {
        var (sin, cos) = Math.SinCos(radians);
        // Cache original values that will be overwritten
        double scaleX = ScaleX, skewX = SkewX;
        double skewY = SkewY, scaleY = ScaleY;
        double offsetX = OffsetX, offsetY = OffsetY;

        // Apply rotation: this = this * Rotate(radians)
        ScaleX = scaleX * cos + skewX * sin;
        SkewX = scaleX * -sin + skewX * cos;
        SkewY = skewY * cos + scaleY * sin;
        ScaleY = skewY * -sin + scaleY * cos;
        OffsetX = offsetX * cos + offsetY * sin;
        OffsetY = offsetX * -sin + offsetY * cos;
    }

    public readonly Matrix2D TranslatePrepend(double x, double y) => Translate(x, y) * this;

    public readonly Matrix2D ScalePrepend(double sx, double sy) => Scale(sx, sy) * this;

    public readonly Matrix2D SkewPrepend(double sx, double sy) => Skew(sx, sy) * this;

    public readonly Matrix2D RotatePrepend(double radians) => Rotate(radians) * this;

    public readonly Matrix2D Lerp(in Matrix2D b, double t)
    {
        double rotA = Math.Atan2(SkewY, ScaleX), rotB = Math.Atan2(b.SkewY, b.ScaleX);
        double delta = rotB - rotA;
        if (delta > Math.PI) delta -= Math.Tau;
        else if (delta < -Math.PI) delta += Math.Tau;
        double sx = Math.Sqrt(ScaleX * ScaleX + SkewY * SkewY).Lerp(Math.Sqrt(b.ScaleX * b.ScaleX + b.SkewY * b.SkewY), t),
            sy = Math.Sqrt(SkewX * SkewX + ScaleY * ScaleY).Lerp(Math.Sqrt(b.SkewX * b.SkewX + b.ScaleY * b.ScaleY), t),
            skewX = SkewX.Lerp(b.SkewX, t), skewY = SkewY.Lerp(b.SkewY, t);
        var (sin, cos) = Math.SinCos(rotA + delta * t);
        return new(
            cos * sx + skewX * sin, -sin * sy + skewY * cos,
            sin * sx + skewX * cos, cos * sy + skewY * sin,
            OffsetX.Lerp(b.OffsetX, t), OffsetY.Lerp(b.OffsetY, t));
    }

    public readonly void Deconstruct(out double scaleX, out double skewX, out double skewY, out double scaleY, out double offsetX, out double offsetY)
    { scaleX = ScaleX; skewX = SkewX; skewY = SkewY; scaleY = ScaleY; offsetX = OffsetX; offsetY = OffsetY; }

    public readonly bool Equals(Matrix2D other) =>
        ScaleX == other.ScaleX && SkewX == other.SkewX &&
        SkewY == other.SkewY && ScaleY == other.ScaleY &&
        OffsetX == other.OffsetX && OffsetY == other.OffsetY;

    public override readonly bool Equals(object? obj) => obj is Matrix2D m && Equals(m);

    public static bool operator ==(Matrix2D left, Matrix2D right) => left.Equals(right);

    public static bool operator !=(Matrix2D left, Matrix2D right) => !(left == right);

    public override readonly int GetHashCode() => HashCode.Combine(ScaleX, SkewX, SkewY, ScaleY, OffsetX, OffsetY);

    public override readonly string ToString() => $"[{ScaleX}, {SkewX}, {OffsetX}; {SkewY}, {ScaleY}, {OffsetY}; 0, 0, 1]";
}
