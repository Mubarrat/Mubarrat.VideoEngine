namespace Mubarrat.VideoEngine.Draw;

public readonly record struct DashSegment : ILerpable<DashSegment>
{
    public readonly double Fill, Gap, CycleLength;

    public DashSegment(double fill, double gap)
    {
        Fill = fill;
        Gap = gap;
        CycleLength = fill + gap;
    }

    public DashSegment Lerp(in DashSegment other, double t) => new(Fill.Lerp(other.Fill, t), Gap.Lerp(other.Gap, t));

    public static DashSegment operator *(DashSegment segment, double scale) => new(segment.Fill * scale, segment.Gap * scale);
    public static DashSegment operator /(DashSegment segment, double scale) => new(segment.Fill / scale, segment.Gap / scale);
}
