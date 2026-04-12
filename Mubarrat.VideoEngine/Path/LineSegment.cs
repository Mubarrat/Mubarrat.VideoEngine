namespace Mubarrat.VideoEngine.Path;

public record LineSegment(Point Point) : PathSegment
{
    public override Point[] Points { get; } = [Point];
}
