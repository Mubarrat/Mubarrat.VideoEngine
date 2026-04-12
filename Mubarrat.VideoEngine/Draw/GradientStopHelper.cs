namespace Mubarrat.VideoEngine.Draw;

internal static class GradientStopHelper
{
    public static Color32 FindColor(this GradientStop[] stops, double t)
    {
        int stopCount = stops.Length;
        if (t <= stops[0].Offset) return stops[0].Color;
        if (t >= stops[^1].Offset) return stops[^1].Color;
        int left = 0, right = stopCount - 1;
        switch (stopCount)
        {
            case < 9: // Linear search
                for (int i = 0; i < stopCount - 1; i++)
                {
                    var a = stops[i];
                    var b = stops[i + 1];
                    if (t >= a.Offset && t <= b.Offset)
                        return a.Color.Lerp(b.Color, (t - a.Offset) / (b.Offset - a.Offset));
                }
                return stops[^1].Color; // Fallback

            case < 255: // Normal binary search
                while (left <= right)
                {
                    int mid = left + right >> 1;
                    var stop = stops[mid];
                    if (t < stop.Offset)
                        right = mid - 1;
                    else if (t > stop.Offset)
                        left = mid + 1;
                    else
                        return stop.Color;
                }
                break;

            default: // Branchless binary search
                while (left < right)
                {
                    int mid = left + right >> 1, mask = t > stops[mid].Offset ? -1 : 0;
                    left = mid + 1 & mask | left & ~mask;
                    right = mid & ~mask | right & mask;
                }
                break;
        }
        // Determine final interval
        int i0 = left - 1, i1 = left;
        if (i0 < 0) i0 = 0;
        if (i1 >= stopCount) i1 = stopCount - 1;
        var stopA = stops[i0];
        var stopB = stops[i1];
        return stopA.Color.Lerp(stopB.Color, (stopB.Offset - stopA.Offset) == 0 ? 0 : (t - stopA.Offset) / (stopB.Offset - stopA.Offset));
    }
}
