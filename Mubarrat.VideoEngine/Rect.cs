using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine;

[StructLayout(LayoutKind.Explicit, Pack = 8, Size = 32)]
public record struct Rect(
    [field: FieldOffset(0)] double X,
    [field: FieldOffset(8)] double Y,
    [field: FieldOffset(16)] double Width,
    [field: FieldOffset(24)] double Height) : ILerpable<Rect>
{
    public Rect(Point location, Size size) : this(location.X, location.Y, size.Width, size.Height) {}
    public Rect(Point topLeft, Point bottomRight) : this(topLeft, (Size)(bottomRight - topLeft)) {}
    public Rect(double uniform) : this(uniform, uniform, uniform, uniform) {}

    public static Rect Empty => new(0);
    public static Rect Universal => new(Point.Minimum, Size.Maximum);
    public static Rect NaN => new(double.NaN);

    [field: FieldOffset(0)] public Point Location;
    [field: FieldOffset(16)] public Size Size;

    [field: FieldOffset(0)] public double Left;
    [field: FieldOffset(8)] public double Top;
    public double Right { readonly get => X + Width; init => Width = value - X; }
    public double Bottom { readonly get => Y + Height; init => Height = value - Y; }
    public readonly Point Center => new(X + Width / 2, Y + Height / 2);

    [field: FieldOffset(0)] public Point TopLeft;
    public Point TopRight { readonly get => new(Right, Top); init => (Right, Top) = value; }
    public Point BottomLeft { readonly get => new(Left, Bottom); init => (Left, Bottom) = value; }
    public Point BottomRight { readonly get => new(Right, Bottom); init => (Right, Bottom) = value; }

    public readonly Rect Lerp(in Rect target, double t) => new(Location.Lerp(target.Location, t), Size.Lerp(target.Size, t));

    public readonly Rect Offset(Vector2D v) => new(X + v.X, Y + v.Y, Width, Height);
    public readonly Rect Add(Vector2D v) => new(X, Y, Width + v.X, Height + v.Y);

    public readonly Rect Inflate(double dx, double dy) =>
        new(X - dx, Y - dy, Width + dx * 2, Height + dy * 2);

    public readonly bool Contains(Point p) => p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;

    public readonly bool Intersects(Rect other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    public readonly Rect Normalized => new(Point.Min(TopLeft, BottomRight), Size.Abs);

    public readonly Rect Union(Rect other)
    {
        Rect self = Normalized; other = other.Normalized;
        return new Rect(Point.Min(self.TopLeft, other.TopLeft), Point.Max(self.BottomRight, other.BottomRight));
    }

    public readonly Rect Intersection(Rect other)
    {
        Rect self = Normalized; other = other.Normalized;
        return new Rect(Point.Max(self.TopLeft, other.TopLeft), Point.Min(self.BottomRight, other.BottomRight));
    }

    public override readonly string ToString() => $"Rect({X}, {Y}, {Width}, {Height})";

    public static Rect operator *(Rect r, Matrix2D m)
    {
        Point p1 = r.TopLeft * m, p2 = r.TopRight * m, p3 = r.BottomLeft * m, p4 = r.BottomRight * m;
        return new Rect(Point.Min(Point.Min(p1, p2), Point.Min(p3, p4)), Point.Max(Point.Max(p1, p2), Point.Max(p3, p4)));
    }

    public static Rect operator /(Rect r, Matrix2D m) => r * m.Inverse;
}
