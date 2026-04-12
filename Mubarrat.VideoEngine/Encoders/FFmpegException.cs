using FFmpeg.AutoGen;
using System.Text;

namespace Mubarrat.VideoEngine.Encoders;

public unsafe class FFmpegException(string? message) : Exception(message)
{
    public int? ErrorCode { get; }

    public FFmpegException() : this("An error occurred in FFmpeg.") {}

    public FFmpegException(int errorCode) : this(GetErrorMessage(errorCode)) => ErrorCode = errorCode;

    private static string GetErrorMessage(int errorCode)
    {
        const int bufSize = 1024;
        byte* p = stackalloc byte[bufSize];
        ffmpeg.av_strerror(errorCode, p, bufSize);
        var msg = Encoding.UTF8.GetString(p, bufSize);
        if (msg.IndexOf('\0') is int idx and >= 0) msg = msg[..idx];
        return $"FFmpeg error {errorCode}: {msg}";
    }
}
