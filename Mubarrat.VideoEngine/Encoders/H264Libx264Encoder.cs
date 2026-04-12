using FFmpeg.AutoGen;

namespace Mubarrat.VideoEngine.Encoders;

public unsafe class H264Libx264Encoder : VideoEncoder
{
    public static readonly EncoderConstructor Constructor = (w, h, fps, path) => new H264Libx264Encoder(w, h, fps, path);

    private readonly AVFrame* frameBgra;
    private readonly AVFrame* frameYuv;
    private readonly SwsContext* swsCtx;

    public H264Libx264Encoder(ushort width, ushort height, ushort fps, string outputPath)
        : base(width, height, fps, outputPath, "libx264", AVPixelFormat.AV_PIX_FMT_YUV420P)
    {
        frameBgra = ffmpeg.av_frame_alloc();
        frameYuv = ffmpeg.av_frame_alloc();

        frameBgra->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        frameBgra->width = width;
        frameBgra->height = height;

        frameYuv->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
        frameYuv->width = width;
        frameYuv->height = height;

        ffmpeg.av_frame_get_buffer(frameYuv, 32).ThrowIfError();

        swsCtx = ffmpeg.sws_getContext(
            width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
            width, height, AVPixelFormat.AV_PIX_FMT_YUV420P,
            (int)SwsFlags.SWS_BILINEAR, null, null, null
        );
    }

    public override void SendFrame(void* bgraPtr)
    {
        frameBgra->data[0] = (byte*)bgraPtr;
        frameBgra->linesize[0] = width * 4;

        ffmpeg.sws_scale(swsCtx, frameBgra->data, frameBgra->linesize, 0, height, frameYuv->data, frameYuv->linesize);
        frameYuv->pts = NextPts();

        SendToEncoder(frameYuv);
    }

    private bool disposed;
    protected override void Dispose(bool disposing)
    {
        if (disposed) return;
        disposed = true;

        if (frameBgra != null || frameYuv != null)
        {
            fixed (AVFrame** p1 = &frameBgra, p2 = &frameYuv)
            {
                ffmpeg.av_frame_free(p1);
                ffmpeg.av_frame_free(p2);
            }
        }

        if (swsCtx != null)
            ffmpeg.sws_freeContext(swsCtx);

        base.Dispose(disposing);
    }
}
