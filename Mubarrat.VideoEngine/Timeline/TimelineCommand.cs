using Mubarrat.VideoEngine.Draw;

namespace Mubarrat.VideoEngine.Timeline;

public abstract class TimelineCommand
{
    public abstract double StartTime { get; }

    public abstract Drawing? Execute(Drawing? prev, double time);
}
