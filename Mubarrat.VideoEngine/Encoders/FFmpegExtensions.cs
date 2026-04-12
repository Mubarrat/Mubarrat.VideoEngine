namespace Mubarrat.VideoEngine.Encoders;

public static class FFmpegExtensions
{
    extension(int error)
    {
        public void ThrowIfError()
        {
            if (error < 0) throw new FFmpegException(error);
        }
    }
}
