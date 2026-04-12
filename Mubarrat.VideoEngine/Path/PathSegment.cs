namespace Mubarrat.VideoEngine.Path;

public abstract record PathSegment
{
    public abstract Point[] Points { get; }
}
