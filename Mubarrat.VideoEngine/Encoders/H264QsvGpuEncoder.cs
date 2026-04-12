using FFmpeg.AutoGen;
using System.Diagnostics;

namespace Mubarrat.VideoEngine.Encoders;

/// <summary>
/// Provides a hardware-accelerated H.264 video encoder using Intel Quick Sync Video (QSV) via FFmpeg, targeting
/// GPU-based encoding for improved performance.
/// </summary>
/// <remarks>This encoder is intended for scenarios where low-latency, high-throughput H.264 encoding is required
/// and compatible Intel hardware is available. It leverages the QSV hardware device and frame contexts to offload
/// encoding tasks to the GPU, reducing CPU usage compared to software encoders. The encoder expects input frames in
/// BGRA pixel format and outputs H.264-encoded video to the specified file path. Thread safety is not guaranteed; use
/// separate instances for concurrent encoding operations.</remarks>
public unsafe class H264QsvGpuEncoder : VideoEncoder
{
    public static readonly EncoderConstructor Constructor = (w, h, fps, path) => new H264QsvGpuEncoder(w, h, fps, path);

    private AVBufferRef* hwDeviceCtx, hwFramesCtxRef;
    private readonly AVFrame* frameBgra, frameQsv;

    public H264QsvGpuEncoder(ushort width, ushort height, ushort fps, string outputPath)
        : base(width, height, fps, outputPath, "h264_qsv", AVPixelFormat.AV_PIX_FMT_QSV)
    {
        frameBgra = ffmpeg.av_frame_alloc();
        frameQsv = ffmpeg.av_frame_alloc();

        frameBgra->format = (int)AVPixelFormat.AV_PIX_FMT_BGRA;
        frameBgra->width = width;
        frameBgra->height = height;

        // frameQsv will be prepared per-frame (hw buffers attached before transfer)
        frameQsv->format = (int)AVPixelFormat.AV_PIX_FMT_QSV;
        frameQsv->width = width;
        frameQsv->height = height;
    }

    protected override void BeforeOpenCodec()
    {
        // create QSV hw device
        AVBufferRef* dev = null;
        ffmpeg.av_hwdevice_ctx_create(&dev, AVHWDeviceType.AV_HWDEVICE_TYPE_QSV, null, null, 0).ThrowIfError();
        hwDeviceCtx = dev;

        // attach a reference of device to codec context so encoder can use it
        codecCtx->hw_device_ctx = ffmpeg.av_buffer_ref(hwDeviceCtx);

        // create HW frames context for QSV surfaces and set software format to BGRA
        hwFramesCtxRef = ffmpeg.av_hwframe_ctx_alloc(codecCtx->hw_device_ctx);
        if (hwFramesCtxRef == null) throw new FFmpegException("Failed to allocate hw frames context");

        var hwFramesCtx = (AVHWFramesContext*)hwFramesCtxRef->data;
        hwFramesCtx->format = codecCtx->pix_fmt; // AV_PIX_FMT_QSV
        hwFramesCtx->sw_format = AVPixelFormat.AV_PIX_FMT_BGRA; // input software format
        hwFramesCtx->width = width;
        hwFramesCtx->height = height;

        ffmpeg.av_hwframe_ctx_init(hwFramesCtxRef).ThrowIfError();

        // Make codec context aware of the hw frames context
        codecCtx->hw_frames_ctx = ffmpeg.av_buffer_ref(hwFramesCtxRef);
    }

    protected override void ConfigureEncoder(AVDictionary** options)
    {
        ffmpeg.av_dict_set(options, "async_depth", "4", 0);
        ffmpeg.av_dict_set(options, "low_power", "1", 0);
    }

    public override void SendFrame(void* bgraPtr)
    {
        // prepare software BGRA frame pointing to provided memory
        frameBgra->data[0] = (byte*)bgraPtr;
        frameBgra->linesize[0] = width * 4;

        // prepare a hw frame and allocate a hw surface for it
        // ensure any previous hw data is cleared
        frameQsv->hw_frames_ctx = ffmpeg.av_buffer_ref(codecCtx->hw_frames_ctx);

        // allocate HW surface for the frame
        ffmpeg.av_hwframe_get_buffer(frameQsv->hw_frames_ctx, frameQsv, 0).ThrowIfError();

        // transfer data from software BGRA frame into the QSV hw frame
        ffmpeg.av_hwframe_transfer_data(frameQsv, frameBgra, 0).ThrowIfError();

        frameQsv->pts = NextPts();

        SendToEncoder(frameQsv);

        // free the reference to hw_frames_ctx held by frameQsv (codec keeps its own refs)
        ffmpeg.av_buffer_unref(&frameQsv->hw_frames_ctx);
    }

    private bool disposed;
    protected override void Dispose(bool disposing)
    {
        if (disposed) return;
        disposed = true;

        if (frameBgra != null || frameQsv != null)
        {
            fixed (AVFrame** p1 = &frameBgra, p2 = &frameQsv)
            {
                ffmpeg.av_frame_free(p1);
                ffmpeg.av_frame_free(p2);
            }
        }

        if (hwFramesCtxRef != null)
        {
            AVBufferRef* tmp = hwFramesCtxRef;
            ffmpeg.av_buffer_unref(&tmp);
            hwFramesCtxRef = null;
        }

        if (hwDeviceCtx != null)
        {
            AVBufferRef* tmp = hwDeviceCtx;
            ffmpeg.av_buffer_unref(&tmp);
            hwDeviceCtx = null;
        }

        base.Dispose(disposing);
    }
}
