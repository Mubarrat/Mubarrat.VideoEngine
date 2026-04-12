namespace Mubarrat.VideoEngine.Path;

public record QuadraticSegment(Point Control, Point End) : PathSegment
{
    public override Point[] Points { get; } = [Control, End];
}
