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

        // Attempt to create a GPU renderer first (providing the target buffer). If unavailable, fallback to CPU DrawingContext.
        IRenderer renderer = new DrawingContext(buffer, width, height);
        try
        {
            double time = frameIndex / (double)Fps;
            foreach (var builder in layers.Where(x => x.StartTime <= time))
                builder.Draw(renderer, time);
        }
        finally
        {
            renderer.Dispose();
            if (renderer is DrawingContext dc)
            {
                // nothing
            }
        }

    }
}
