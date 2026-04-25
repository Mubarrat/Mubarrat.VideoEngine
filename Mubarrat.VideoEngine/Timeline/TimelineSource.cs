using Mubarrat.VideoEngine.Draw;

namespace Mubarrat.VideoEngine.Timeline;

public class TimelineSource(int fps) : IFrameSource
{
    private readonly List<TimelineLayer> layers = [];

    public int Fps { get; } = fps;

    public TimelineLayer NewLayer => new TimelineLayer().With(layers.Add);

    public unsafe void RenderFrame(uint frameIndex, Color32* buffer, ushort width, ushort height)
    {
        foreach (var layer in layers)
            layer.UpdateLayout(new(width, height));
        DrawingContext drawingContext = new(buffer, width, height);
        double time = frameIndex / (double)Fps;
        foreach (var builder in layers.Where(x => x.StartTime <= time))
            builder.Draw(drawingContext, time);
    }
}
