namespace Mubarrat.VideoEngine.Draw;

public interface IBrush : ILerpable<IBrush>
{
    /// <param name="x">0..1</param>
    /// <param name="y">0..1</param>
    Color32 Sample(double x, double y);

    IBrush ILerpable<IBrush>.Lerp(in IBrush other, double t) => new LerpBrush(this, other, t);

    public static readonly IBrush Transparent = new SolidColorBrush();
}
