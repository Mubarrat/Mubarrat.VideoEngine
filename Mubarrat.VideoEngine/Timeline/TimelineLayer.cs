using Mubarrat.VideoEngine.Draw;
using Mubarrat.VideoEngine.Objects;

namespace Mubarrat.VideoEngine.Timeline;

public class TimelineLayer
{
    public double StartTime => commands.Count == 0 ? 0 : commands.Min(x => x.StartTime);

    private readonly List<TimelineCommand> commands = [];

    public TimelineLayer To(double second, Drawing drawing)
    {
        commands.Add(new ToTimelineCommand(second, drawing));
        return this;
    }

    public TimelineLayer To(double second, FrameworkObject frameworkObject)
    {
        commands.Add(new ToFrameworkObjectTimelineCommand(second, frameworkObject));
        return this;
    }

    private class ToTimelineCommand(double second, Drawing drawing) : TimelineCommand
    {
        public override double StartTime => second;

        public override BaseObject? Execute(BaseObject? prev, double time) => time < second ? prev : (Drawing)drawing.Clone();
    }

    private class ToFrameworkObjectTimelineCommand(double second, FrameworkObject frameworkObject) : TimelineCommand
    {
        public override double StartTime => second;

        public override BaseObject? Execute(BaseObject? prev, double time) => time < second ? prev : (FrameworkObject)frameworkObject.Clone();
    }

    public TimelineLayer To(double fromSecond, double toSecond, Drawing drawing, IAnimator<Drawing> animator = null!, EasingFunction easingFunction = null!)
    {
        animator ??= Lerper<Drawing>.Instance;
        easingFunction ??= EasingFunctions.Linear;
        commands.Add(new ToWithMotionTimelineCommand(fromSecond, toSecond, drawing, animator, easingFunction));
        return this;
    }

    public TimelineLayer To(double fromSecond, double toSecond, FrameworkObject frameworkObject, IAnimator<FrameworkObject> animator = null!, EasingFunction easingFunction = null!)
    {
        ArgumentNullException.ThrowIfNull(frameworkObject);
        animator ??= Lerper<FrameworkObject>.Instance;
        easingFunction ??= EasingFunctions.Linear;
        commands.Add(new ToFrameworkObjectWithMotionTimelineCommand(fromSecond, toSecond, frameworkObject, animator, easingFunction));
        return this;
    }

    private class ToWithMotionTimelineCommand(double fromSecond, double toSecond, Drawing drawing, IAnimator<Drawing> animator = null!, EasingFunction easingFunction = null!) : TimelineCommand
    {
        public override double StartTime => fromSecond;

        public override BaseObject? Execute(BaseObject? prev, double time)
        {
            if (time <= fromSecond)
                return prev;
            if (time >= toSecond)
                return (Drawing)drawing.Clone();
            return animator.Animate(prev switch
            {
                Drawing d => d,
                FrameworkObject f => f.ToDrawing(),
                null => new PathDrawing(),
                _ => throw new InvalidOperationException("Previous drawing must be of type Drawing, FrameworkObject, or null")
            }, drawing, easingFunction((time - fromSecond) / (toSecond - fromSecond)));
        }
    }

    private class ToFrameworkObjectWithMotionTimelineCommand(double fromSecond, double toSecond, FrameworkObject frameworkObject, IAnimator<FrameworkObject> animator = null!, EasingFunction easingFunction = null!) : TimelineCommand
    {
        public override double StartTime => fromSecond;

        public override BaseObject? Execute(BaseObject? prev, double time)
        {
            if (time <= fromSecond)
                return prev;
            if (time >= toSecond)
                return (FrameworkObject)frameworkObject.Clone();
            return animator.Animate(prev switch
            {
                FrameworkObject f => f,
                Drawing d => new DrawingWrapperFrameworkObject(d),
                null => new Border(),
                _ => throw new InvalidOperationException("Previous drawing must be of type Drawing, FrameworkObject, or null")
            }, frameworkObject, easingFunction((time - fromSecond) / (toSecond - fromSecond))).ToDrawing();
        }
    }

    public TimelineLayer Set(double second, Property property, object value)
    {
        commands.Add(new SetTimelineCommand(second, property, value));
        return this;
    }

    private class SetTimelineCommand(double second, Property property, object value) : TimelineCommand
    {
        public override double StartTime => second;

        public override BaseObject? Execute(BaseObject? prev, double time)
        {
            if (time >= second)
                prev?[property] = value;
            return prev;
        }
    }

    public TimelineLayer Set<T>(double fromSecond, double toSecond, Property property, T value, IAnimator<T> animator = null!, EasingFunction easingFunction = null!)
    {
        animator ??= (IAnimator<T>)typeof(Lerper<>).MakeGenericType(property.PropertyType).GetProperty(nameof(Lerper<>.Instance))!.GetValue(null)!;
        easingFunction ??= EasingFunctions.Linear;
        commands.Add(new SetWithMotionTimelineCommand<T>(fromSecond, toSecond, property, value, animator, easingFunction));
        return this;
    }

    private class SetWithMotionTimelineCommand<T>(double fromSecond, double toSecond, Property property, T value, IAnimator<T> animator = null!, EasingFunction easingFunction = null!) : TimelineCommand
    {
        public override double StartTime => fromSecond;

        public override BaseObject? Execute(BaseObject? prev, double time)
        {
            if (time > fromSecond)
            {
                prev?[property] = time >= toSecond
                    ? value
                    : animator.Animate((T)prev?[property], value, easingFunction((time - fromSecond) / (toSecond - fromSecond)));
            }
            return prev;
        }
    }

    public void Draw(DrawingContext drawingContext, double time)
    {
        switch (commands.Where(x => x.StartTime <= time).Aggregate(null as BaseObject, (prev, command) => command.Execute(prev, time)))
        {
            case Drawing drawing:
                drawingContext.Draw(drawing);
                break;
            case FrameworkObject frameworkObject:
                drawingContext.Draw(frameworkObject.ToDrawing());
                break;
        }
    }
}
