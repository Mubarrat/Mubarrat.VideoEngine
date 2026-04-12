using FFmpeg.AutoGen;

namespace Mubarrat.VideoEngine.Encoders;

public unsafe abstract class VideoEncoder : IDisposable
{
    protected readonly AVFormatContext* fmtCtx;
    protected readonly AVCodecContext* codecCtx;
    protected readonly AVStream* stream;
    protected readonly AVPacket* pkt;
    private long pts;

    protected readonly ushort width, height;

    protected VideoEncoder(ushort width, ushort height, ushort fps, string outputPath, string codecName, AVPixelFormat targetFormat)
    {
        this.width = width;
        this.height = height;

        // Allocate format context
        fixed (AVFormatContext** pFmt = &fmtCtx)
            ffmpeg.avformat_alloc_output_context2(pFmt, null, null, outputPath);
        if (fmtCtx == null) throw new Exception("Failed to allocate output context");

        // Find encoder
        var codec = ffmpeg.avcodec_find_encoder_by_name(codecName);
        if (codec == null) throw new Exception($"Encoder '{codecName}' not found");

        stream = ffmpeg.avformat_new_stream(fmtCtx, codec);

        codecCtx = ffmpeg.avcodec_alloc_context3(codec);
        codecCtx->width = width;
        codecCtx->height = height;
        codecCtx->time_base = new AVRational { num = 1, den = fps };
        codecCtx->framerate = new AVRational { num = fps, den = 1 };
        codecCtx->pix_fmt = targetFormat;
        codecCtx->bit_rate = 4_000_000;

        if ((fmtCtx->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            codecCtx->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

        // Allow derived classes to setup hw device / frames
        BeforeOpenCodec();

        AVDictionary* options = null;
        ConfigureEncoder(&options);

        ffmpeg.avcodec_open2(codecCtx, codec, &options).ThrowIfError();
        ffmpeg.avcodec_parameters_from_context(stream->codecpar, codecCtx).ThrowIfError();

        stream->time_base = codecCtx->time_base;

        if ((fmtCtx->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            ffmpeg.avio_open(&fmtCtx->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE).ThrowIfError();

        ffmpeg.avformat_write_header(fmtCtx, null).ThrowIfError();

        pkt = ffmpeg.av_packet_alloc();
    }

    /// <summary>
    /// Configures encoder-specific options before encoding begins.
    /// </summary>
    /// <remarks>Override this method in a derived class to customize encoder configuration. This method is
    /// called before the encoding process starts, allowing subclasses to modify or add options as needed.</remarks>
    /// <param name="options">A pointer to an AVDictionary containing key-value pairs that specify encoder options. May be null if no options
    /// are provided.</param>
    protected virtual void ConfigureEncoder(AVDictionary** options) { }

    /// <summary>
    /// Override in derived classes to setup hw device / frames context
    /// before calling avcodec_open2()
    /// </summary>
    protected virtual void BeforeOpenCodec() { }

    /// <summary>
    /// Send a frame to the encoder. Must be implemented by derived classes to handle pixel conversion.
    /// </summary>
    public abstract void SendFrame(void* data);

    protected void SendToEncoder(AVFrame* frame)
    {
        ffmpeg.avcodec_send_frame(codecCtx, frame).ThrowIfError();

        while (true)
        {
            int ret = ffmpeg.avcodec_receive_packet(codecCtx, pkt);
            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF) break;
            ret.ThrowIfError();

            ffmpeg.av_packet_rescale_ts(pkt, codecCtx->time_base, stream->time_base);
            pkt->stream_index = stream->index;

            ffmpeg.av_interleaved_write_frame(fmtCtx, pkt).ThrowIfError();
            ffmpeg.av_packet_unref(pkt);
        }
    }

    protected long NextPts() => pts++;

    private bool disposed;
    protected virtual void Dispose(bool disposing)
    {
        if (disposed) return;
        disposed = true;

        // No managed resources to free yet
        if (disposing)
        {
            // free managed resources if you add any in future
        }

        // Free unmanaged resources
        SendToEncoder(null); // flush
        ffmpeg.av_write_trailer(fmtCtx);

        if ((fmtCtx->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0)
            ffmpeg.avio_closep(&fmtCtx->pb);

        fixed (AVCodecContext** pCtx = &codecCtx) ffmpeg.avcodec_free_context(pCtx);
        fixed (AVPacket** pPkt = &pkt) ffmpeg.av_packet_free(pPkt);
        ffmpeg.avformat_free_context(fmtCtx);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~VideoEncoder()
    {
        Dispose(false);
    }
}
