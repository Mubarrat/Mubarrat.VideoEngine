using Mubarrat.VideoEngine.Draw;
using Mubarrat.VideoEngine.Objects;
using System.Linq;

namespace Mubarrat.VideoEngine.Timeline;

public class TimelineLayer
{
    public double StartTime => commands.Count == 0 ? 0 : commands.Min(x => x.StartTime);

    private readonly List<TimelineCommand> commands = [];

    private bool layoutUpdated = false;

    internal void UpdateLayout(Size availableSize)
    {
        lock (this)
        {
            if (layoutUpdated) return;
            foreach (var command in commands)
            {
                switch (command)
                {
                    case ToFrameworkObjectTimelineCommand com:
                        UpdateRootFrameworkObjectLayout(com.FrameworkObject, availableSize);
                        break;
                    case ToFrameworkObjectWithMotionTimelineCommand com:
                        UpdateRootFrameworkObjectLayout(com.FrameworkObject, availableSize);
                        break;
                }
            }
            layoutUpdated = true;
        }
    }

    private static void UpdateRootFrameworkObjectLayout(FrameworkObject frameworkObject, Size availableSize)
    {
        frameworkObject.MeasureSubtree(availableSize);

        double availableWidth = Math.Max(0, availableSize.Width);
        double availableHeight = Math.Max(0, availableSize.Height);

        double desiredWidth = double.IsFinite(frameworkObject.DesiredSize.Width)
            ? Math.Max(0, frameworkObject.DesiredSize.Width)
            : 0;

        double desiredHeight = double.IsFinite(frameworkObject.DesiredSize.Height)
            ? Math.Max(0, frameworkObject.DesiredSize.Height)
            : 0;

        double arrangedWidth = frameworkObject.HorizontalAlignment == HorizontalAlignment.Stretch
            ? availableWidth
            : Math.Min(availableWidth, desiredWidth);

        double arrangedHeight = frameworkObject.VerticalAlignment == VerticalAlignment.Stretch
            ? availableHeight
            : Math.Min(availableHeight, desiredHeight);

        double dx = frameworkObject.HorizontalAlignment switch
        {
            HorizontalAlignment.Center => (availableWidth - arrangedWidth) * 0.5,
            HorizontalAlignment.Right => availableWidth - arrangedWidth,
            _ => 0
        };

        double dy = frameworkObject.VerticalAlignment switch
        {
            VerticalAlignment.Middle => (availableHeight - arrangedHeight) * 0.5,
            VerticalAlignment.Bottom => availableHeight - arrangedHeight,
            _ => 0
        };

        frameworkObject.ArrangeSubtree(new Size(arrangedWidth, arrangedHeight), Matrix2D.Translate(dx, dy));
    }

    public TimelineLayer To(double second, Drawing drawing)
    {
        commands.Add(new ToTimelineCommand(second, drawing));
        return this;
    }

    private class ToTimelineCommand(double second, Drawing drawing) : TimelineCommand
    {
        public override double StartTime => second;

        public override BaseObject? Execute(BaseObject? prev, double time) => time < second ? prev : (Drawing)drawing.Clone();
    }

    public TimelineLayer To(double second, FrameworkObject frameworkObject)
    {
        commands.Add(new ToFrameworkObjectTimelineCommand(second, frameworkObject));
        return this;
    }

    private class ToFrameworkObjectTimelineCommand(double second, FrameworkObject frameworkObject) : TimelineCommand
    {
        public override double StartTime => second;

        public FrameworkObject FrameworkObject { get; } = frameworkObject;

        public override BaseObject? Execute(BaseObject? prev, double time) => time < second ? prev : (FrameworkObject)FrameworkObject.Clone();
    }

    public TimelineLayer To(double fromSecond, double toSecond, Drawing drawing, Animator<Drawing> animator = null!, EasingFunction easingFunction = null!)
    {
        animator ??= Animators.Morph;
        easingFunction ??= EasingFunctions.Linear;
        commands.Add(new ToWithMotionTimelineCommand(fromSecond, toSecond, drawing, animator, easingFunction));
        return this;
    }

    private class ToWithMotionTimelineCommand(double fromSecond, double toSecond, Drawing drawing, Animator<Drawing> animator = null!, EasingFunction easingFunction = null!) : TimelineCommand
    {
        public override double StartTime => fromSecond;

        public override BaseObject? Execute(BaseObject? prev, double time)
        {
            if (time <= fromSecond)
                return prev;
            if (time >= toSecond)
                return (Drawing)drawing.Clone();
            return animator(prev switch
            {
                Drawing d => d,
                FrameworkObject f => f.ToDrawing(),
                null => new PathDrawing(),
                _ => throw new InvalidOperationException("Previous drawing must be of type Drawing, FrameworkObject, or null")
            }, drawing, easingFunction((time - fromSecond) / (toSecond - fromSecond)));
        }
    }

    public TimelineLayer To(double fromSecond, double toSecond, FrameworkObject frameworkObject, Animator<FrameworkObject> animator = null!, EasingFunction easingFunction = null!)
    {
        ArgumentNullException.ThrowIfNull(frameworkObject);
        animator ??= Animators.Morph;
        easingFunction ??= EasingFunctions.Linear;
        commands.Add(new ToFrameworkObjectWithMotionTimelineCommand(fromSecond, toSecond, frameworkObject, animator, easingFunction));
        return this;
    }

    private class ToFrameworkObjectWithMotionTimelineCommand(double fromSecond, double toSecond, FrameworkObject frameworkObject, Animator<FrameworkObject> animator = null!, EasingFunction easingFunction = null!) : TimelineCommand
    {
        public override double StartTime => fromSecond;

        public FrameworkObject FrameworkObject { get; } = frameworkObject;

        public override BaseObject? Execute(BaseObject? prev, double time)
        {
            if (time <= fromSecond)
                return prev;
            if (time >= toSecond)
                return (FrameworkObject)FrameworkObject.Clone();
            return animator(prev switch
            {
                FrameworkObject f => f,
                Drawing d => new DrawingWrapperFrameworkObject(d),
                null => new Border(),
                _ => throw new InvalidOperationException("Previous drawing must be of type Drawing, FrameworkObject, or null")
            }, FrameworkObject, easingFunction((time - fromSecond) / (toSecond - fromSecond)));
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

    public TimelineLayer Set<T>(double fromSecond, double toSecond, Property property, T value, Animator<T> animator = null!, EasingFunction easingFunction = null!)
    {
        if (animator is null)
        {
            var methods = typeof(Animators).GetMethods().Where(method => method.Name == nameof(Animators.Morph));
            animator = (Animator<T>)Delegate.CreateDelegate(typeof(Animator<T>),
                methods.FirstOrDefault(method => !method.IsGenericMethod && method.ReturnType == typeof(T)) ??
                methods.FirstOrDefault(method => method.IsGenericMethod)?.MakeGenericMethod(typeof(T))!);
        }
        easingFunction ??= EasingFunctions.Linear;
        commands.Add(new SetWithMotionTimelineCommand<T>(fromSecond, toSecond, property, value, animator, easingFunction));
        return this;
    }

    private class SetWithMotionTimelineCommand<T>(double fromSecond, double toSecond, Property property, T value, Animator<T> animator = null!, EasingFunction easingFunction = null!) : TimelineCommand
    {
        public override double StartTime => fromSecond;

        public override BaseObject? Execute(BaseObject? prev, double time)
        {
            if (time > fromSecond)
            {
                prev?[property] = time >= toSecond
                    ? value
                    : animator((T)prev?[property], value, easingFunction((time - fromSecond) / (toSecond - fromSecond)));
            }
            return prev;
        }
    }

    // an intent where object is animated driven by property, ToWithProperty
    public TimelineLayer ToWithProperty<T>(double fromSecond, double toSecond, Property property, T value, Animator<FrameworkObject> animator = null!, EasingFunction easingFunction = null!)
    {
        return this;
    }

    public TimelineLayer ToDrawingWithProperty<T>(double fromSecond, double toSecond, Property property, T value, Animator<Drawing> animator = null!, EasingFunction easingFunction = null!)
    {
        return this;
    }

    public void Draw(DrawingContext drawingContext, double time)
    {
        switch (commands.Where(x => x.StartTime <= time).Aggregate(null as BaseObject, (prev, command) => command.Execute(prev, time)))
        {
            case Drawing drawing:
                drawingContext.Draw(drawing);
                break;
            case FrameworkObject frameworkObject:
                //if (hasAvailableSize)
                //    UpdateRootFrameworkObjectLayout(frameworkObject, latestAvailableSize);
                drawingContext.Draw(frameworkObject.ToDrawing());
                break;
        }
    }
}
