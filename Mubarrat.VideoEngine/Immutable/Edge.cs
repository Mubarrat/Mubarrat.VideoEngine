using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Immutable;

[StructLayout(LayoutKind.Explicit, Pack = 8, Size = 32)]
public struct Edge : IEquatable<Edge>, ILerpable<Edge>
{
    [FieldOffset(0)] public Point Point1;
    [FieldOffset(16)] public Point Point2;

    [FieldOffset(0)] public double X1;
    [FieldOffset(8)] public double Y1;
    [FieldOffset(16)] public double X2;
    [FieldOffset(24)] public double Y2;

    public readonly double HorizontalDelta
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => X2 - X1;
    }

    public readonly double VerticalDelta
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Y2 - Y1;
    }

    public readonly double LengthSquared
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            double h = HorizontalDelta, v = VerticalDelta;
            return Math.FusedMultiplyAdd(h, h, v * v); // better than h * h + v * v in terms of precision and performance
        }
    }

    public readonly double Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Math.Sqrt(LengthSquared);
    }

    public readonly Point MidPoint
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => new((X1 + X2) / 2, (Y1 + Y2) / 2);
    }

    public Edge(Point point1, Point point2) => (Point1, Point2) = (point1, point2);

    public Edge(Point singlePoint) : this(singlePoint, singlePoint) {}

    public Edge(double x1, double y1, double x2, double y2) => (X1, Y1, X2, Y2) = (x1, y1, x2, y2);

    public static Edge operator *(Edge left, Matrix2D matrix) => new(left.Point1 * matrix, left.Point2 * matrix);
    public static Edge operator /(Edge left, Matrix2D matrix) => new(left.Point1 / matrix, left.Point2 / matrix);
    public void operator *=(Matrix2D matrix)
    {
        Point1 *= matrix;
        Point2 *= matrix;
    }
    public void operator /=(Matrix2D matrix)
    {
        Point1 /= matrix;
        Point2 /= matrix;
    }

    public readonly bool Equals(Edge other) => Point1 == other.Point1 && Point2 == other.Point2;

    public override readonly bool Equals(object? obj) => obj is Edge e && Equals(e);

    public static bool operator ==(Edge left, Edge right) => left.Equals(right);

    public static bool operator !=(Edge left, Edge right) => !(left == right);

    public override readonly int GetHashCode() => HashCode.Combine(Point1, Point2);

    public readonly Edge Lerp(in Edge other, double t) => new(Point1.Lerp(other.Point1, t), Point2.Lerp(other.Point2, t));

    public readonly void Deconstruct(out Point point1, out Point point2) => (point1, point2) = (Point1, Point2);

    public readonly void Deconstruct(out double x1, out double y1, out double x2, out double y2) => (x1, y1, x2, y2) = (X1, Y1, X2, Y2);

    public override readonly string ToString() => $"Edge({Point1}, {Point2})";
}
