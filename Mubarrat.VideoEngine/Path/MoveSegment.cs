namespace Mubarrat.VideoEngine.Path;

public record MoveSegment(Point Point) : PathSegment
{
    public override Point[] Points { get; } = [Point];
}
