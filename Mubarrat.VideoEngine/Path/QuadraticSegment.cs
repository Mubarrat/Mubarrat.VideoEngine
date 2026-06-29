namespace Mubarrat.VideoEngine.Path;

public readonly record struct QuadraticSegment(Point Start, Point Control, Point End) : IPathSegment
{
    public readonly Rect Bounds { get; } = ComputeBounds(Start, Control, End);

    private static Rect ComputeBounds(Point p0, Point p1, Point p2)
    {
        double minX = Math.Min(p0.X, p2.X), minY = Math.Min(p0.Y, p2.Y), maxX = Math.Max(p0.X, p2.X), maxY = Math.Max(p0.Y, p2.Y);
        IncludeExtrema(p0.X, p1.X, p2.X, ref minX, ref maxX);
        IncludeExtrema(p0.Y, p1.Y, p2.Y, ref minY, ref maxY);
        return new(minX, minY, maxX - minX, maxY - minY);
    }

    private static void IncludeExtrema(double p0, double p1, double p2, ref double min, ref double max)
    {
        double denom = p0 - 2 * p1 + p2;
        if (Math.Abs(denom) < 1e-12)
            return;
        double t = (p0 - p1) / denom;
        if (t <= 0 || t >= 1)
            return;
        double mt = 1 - t, value = mt * mt * p0 + 2 * mt * t * p1 + t * t * p2;
        if (value < min) min = value;
        if (value > max) max = value;
    }

    public IPathSegment Lerp(in IPathSegment other, double t) => t switch
    {
        0 => this,
        1 => other,
        _ => other switch
        {
            LineSegment l => new QuadraticSegment(
                Start.Lerp(l.Start, t),
                Control.Lerp(l.Start.Lerp(l.End, 0.5), t),
                End.Lerp(l.End, t)),
            QuadraticSegment q => new QuadraticSegment(
                Start.Lerp(q.Start, t),
                Control.Lerp(q.Control, t),
                End.Lerp(q.End, t)),
            CubicSegment c => new CubicSegment(
                Start.Lerp(c.Start, t),
                Control.Lerp(c.Control1, t),
                Control.Lerp(c.Control2, t),
                End.Lerp(c.End, t)),
            _ => throw new NotSupportedException($"Lerp is not supported between {this.GetType().Name} and {other.GetType().Name}"),
        }
    };
}
