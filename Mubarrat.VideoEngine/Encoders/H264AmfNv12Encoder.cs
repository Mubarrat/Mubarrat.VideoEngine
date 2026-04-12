using FFmpeg.AutoGen;

namespace Mubarrat.VideoEngine.Encoders;

public unsafe class H264AmfNv12Encoder : VideoEncoder
{
    public static readonly EncoderConstructor Constructor = (w, h, fps, path) => new H264AmfNv12Encoder(w, h, fps, path);

    private readonly AVFrame* frameBgra;
    private readonly AVFrame* frameNv12;
    private readonly SwsContext* swsCtx;

    public H264AmfNv12Encoder(ushort width, ushort height, ushort fps, string outputPath)
        : base(width, height, fps, outputPath, "h264_amf", AVPixelFormat.AV_PIX_FMT_NV12)
    {
        frameBgra = ffmpeg.av_frame_alloc();
        frameNv12 = ffmpeg.av_frame_alloc();

        frameBgra->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        frameBgra->width = width;
        frameBgra->height = height;

        frameNv12->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
        frameNv12->width = width;
        frameNv12->height = height;

        ffmpeg.av_frame_get_buffer(frameNv12, 32).ThrowIfError();

        swsCtx = ffmpeg.sws_getContext(
            width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
            width, height, AVPixelFormat.AV_PIX_FMT_NV12,
            (int)SwsFlags.SWS_FAST_BILINEAR, null, null, null
        );
    }

    public override void SendFrame(void* bgraPtr)
    {
        frameBgra->data[0] = (byte*)bgraPtr;
        frameBgra->linesize[0] = width * 4;

        ffmpeg.sws_scale(swsCtx, frameBgra->data, frameBgra->linesize, 0, height, frameNv12->data, frameNv12->linesize);
        frameNv12->pts = NextPts();

        SendToEncoder(frameNv12);
    }

    private bool disposed;
    protected override void Dispose(bool disposing)
    {
        if (disposed) return;
        disposed = true;

        if (frameBgra != null || frameNv12 != null)
        {
            fixed (AVFrame** p1 = &frameBgra, p2 = &frameNv12)
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
