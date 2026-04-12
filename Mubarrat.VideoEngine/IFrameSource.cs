namespace Mubarrat.VideoEngine;

public interface IFrameSource
{
    unsafe void RenderFrame(uint frameIndex, Color32* buffer, ushort width, ushort height);
}
