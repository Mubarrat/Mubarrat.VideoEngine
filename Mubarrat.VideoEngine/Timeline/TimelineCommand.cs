using Mubarrat.VideoEngine.Objects;

namespace Mubarrat.VideoEngine.Timeline;

public abstract class TimelineCommand
{
    public abstract double StartTime { get; }

    public abstract BaseObject? Execute(BaseObject? prev, double time);
}
