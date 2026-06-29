namespace Mubarrat.VideoEngine.Path;

public readonly record struct LineSegment(Point Start, Point End) : IPathSegment
{
    public readonly Rect Bounds { get; } = new Rect(Start, End).Normalized;

    public IPathSegment Lerp(in IPathSegment other, double t) => t switch
    {
        0 => this,
        1 => other,
        _ => other switch
        {
            LineSegment l => new LineSegment(
                Start.Lerp(l.Start, t),
                End.Lerp(l.End, t)),
            QuadraticSegment q => new QuadraticSegment(
                Start.Lerp(q.Start, t),
                Start.Lerp(End, 0.5).Lerp(q.Control, t),
                End.Lerp(q.End, t)),
            CubicSegment c => new CubicSegment(
                Start.Lerp(c.Start, t),
                Start.Lerp(End, 1/3).Lerp(c.Control1, t),
                End.Lerp(Start, 1/3).Lerp(c.Control2, t),
                End.Lerp(c.End, t)),
            _ => throw new NotSupportedException($"Lerp is not supported between {this.GetType().Name} and {other.GetType().Name}"),
        }
    };
}
