namespace Mubarrat.VideoEngine.Path;

public readonly record struct MoveSegment(Point Point) : IPathSegment
{
    public readonly Point Start => Point;
    public readonly Point End => Point;
    public readonly Rect Bounds { get; } = new(Point, Size.Zero);

    public IPathSegment Lerp(in IPathSegment other, double t) => other is MoveSegment move ? new MoveSegment(Point.Lerp(move.Point, t)) : throw new NotSupportedException($"Lerp is not supported between {this.GetType().Name} and {other.GetType().Name}");
}
