using FFmpeg.AutoGen;

namespace Mubarrat.VideoEngine.Encoders;

public static unsafe class EncoderFactory
{
    // Map encoder names to constructors
    private static readonly Dictionary<string, EncoderConstructor> EncoderMap = new()
    {
        ["h264_qsv"] = H264QsvGpuEncoder.Constructor,
        ["h264_nvenc"] = H264NvencNv12Encoder.Constructor,
        ["h264_vaapi"] = H264VaapiNv12Encoder.Constructor,
        ["h264_amf"] = H264AmfNv12Encoder.Constructor,
        ["h264_videotoolbox"] = H264VtNv12Encoder.Constructor,
        ["libx264"] = H264Libx264Encoder.Constructor
    };

    // Preferred order for speed/fallback
    private static readonly string[] PreferredOrder =
    [
        "h264_qsv",
        "h264_nvenc",
        "h264_vaapi",
        "h264_amf",
        "h264_videotoolbox",
        "libx264"
    ];

    public static EncoderConstructor CreateBestEncoder()
    {
        // Detect all available encoders on this system
        var availableEncoders = new HashSet<string>();
        void* iter = null;
        AVCodec* codecPtr;
        while ((codecPtr = ffmpeg.av_codec_iterate(&iter)) != null)
        {
            if (codecPtr->id == AVCodecID.AV_CODEC_ID_H264)
            {
                int count = 0;
                for (var p = codecPtr->name; *p != 0; p++)
                    count++;
                var name = System.Text.Encoding.UTF8.GetString(codecPtr->name, count);
                availableEncoders.Add(name);
            }
        }

        // Pick the first preferred encoder that is actually available
        foreach (var name in PreferredOrder)
        {
            if (availableEncoders.Contains(name) && EncoderMap.TryGetValue(name, out var ctor))
                return ctor;
        }

        throw new Exception("No suitable H.264 encoder found on this system.");
    }
}
