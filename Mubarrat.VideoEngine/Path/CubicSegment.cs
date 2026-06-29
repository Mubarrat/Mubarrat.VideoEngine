namespace Mubarrat.VideoEngine.Path;

public readonly record struct CubicSegment(Point Start, Point Control1, Point Control2, Point End) : IPathSegment
{
    public Rect Bounds { get; } = ComputeBounds(Start, Control1, Control2, End);

    private static Rect ComputeBounds(Point p0, Point p1, Point p2, Point p3)
    {
        double minX = Math.Min(Math.Min(p0.X, p3.X), Math.Min(p1.X, p2.X)), minY = Math.Min(Math.Min(p0.Y, p3.Y), Math.Min(p1.Y, p2.Y)), maxX = Math.Max(Math.Max(p0.X, p3.X), Math.Max(p1.X, p2.X)), maxY = Math.Max(Math.Max(p0.Y, p3.Y), Math.Max(p1.Y, p2.Y));
        ExpandAxis(p0.X, p1.X, p2.X, p3.X, ref minX, ref maxX);
        ExpandAxis(p0.Y, p1.Y, p2.Y, p3.Y, ref minY, ref maxY);
        return new(minX, minY, maxX - minX, maxY - minY);
    }

    private static void ExpandAxis(double p0, double p1, double p2, double p3, ref double min, ref double max)
    {
        double a = -p0 + 3 * p1 - 3 * p2 + p3, b = 2 * (p0 - 2 * p1 + p2), c = -p0 + p1;
        const double eps = 1e-12;
        if (Math.Abs(a) < eps)
        {
            if (Math.Abs(b) < eps)
                return;
            Eval(p0, p1, p2, p3, -c / b, ref min, ref max);
            return;
        }
        double discriminant = b * b - 4 * a * c;
        if (discriminant < 0)
            return;
        double sqrt = Math.Sqrt(discriminant), inv2a = 1.0 / (2.0 * a), t1 = (-b + sqrt) * inv2a, t2 = (-b - sqrt) * inv2a;
        Eval(p0, p1, p2, p3, t1, ref min, ref max);
        Eval(p0, p1, p2, p3, t2, ref min, ref max);
    }

    private static void Eval(double p0, double p1, double p2, double p3, double t, ref double min, ref double max)
    {
        if ((uint)(t > 0 ? 1 : 0) == 0 || (uint)(t < 1 ? 1 : 0) == 0)
            return;
        double mt = 1 - t, mtmt = mt * mt, tt = t * t, value = mtmt * mt * p0 + 3 * mtmt * t * p1 + 3 * mt * tt * p2 + tt * t * p3;
        if (value < min) min = value;
        if (value > max) max = value;
    }

    public IPathSegment Lerp(in IPathSegment other, double t) => t switch
    {
        0 => this,
        1 => other,
        _ => other switch
        {
            LineSegment l => new CubicSegment(
                Start.Lerp(l.Start, t),
                Control1.Lerp(l.Start.Lerp(l.End, 1 / 3), t),
                Control2.Lerp(l.End.Lerp(l.Start, 1 / 3), t),
                End.Lerp(l.End, t)),
            QuadraticSegment q => new CubicSegment(
                Start.Lerp(q.Start, t),
                Control1.Lerp(q.Control, t),
                Control2.Lerp(q.Control, t),
                End.Lerp(q.End, t)),
            CubicSegment c => new CubicSegment(
                Start.Lerp(c.Start, t),
                Control1.Lerp(c.Control1, t),
                Control2.Lerp(c.Control2, t),
                End.Lerp(c.End, t)),
            _ => throw new NotSupportedException($"Cannot lerp {GetType().Name} with {other.GetType().Name}"),
        }
    };
}
