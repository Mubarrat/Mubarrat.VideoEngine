namespace Mubarrat.VideoEngine.Path;

public record CubicSegment(Point Control1, Point Control2, Point End) : PathSegment
{
    public override Point[] Points { get; } = [Control1, Control2, End];
}
