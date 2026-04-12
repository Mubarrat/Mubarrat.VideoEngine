namespace Mubarrat.VideoEngine.Draw;

public readonly record struct Pen(
    IBrush Brush,
    double Thickness,
    LineCap Cap = LineCap.Round,
    LineJoin Join = LineJoin.Miter,
    double[]? DashPattern = null
) : ILerpable<Pen>
{
    public Color32 Sample(double x, double y) => Brush.Sample(x, y);

    public Pen Lerp(in Pen other, double t) => new(
        Brush.Lerp(other.Brush, t),
        Thickness.Lerp(other.Thickness, t),
        t < 0.5 ? Cap : other.Cap,
        t < 0.5 ? Join : other.Join,
        LerpDash(DashPattern, other.DashPattern, t));

    private static double[]? LerpDash(double[]? a, double[]? b, double t)
    {
        if (t == 0) return a;
        else if (t == 1) return b;
        switch (a, b)
        {
            case (null, null): return null; // both are lines
            case (null, not null): // self is line, other is dashed
                {
                    // lerp odd ones from 0 to them
                    double[] res = new double[b.Length];
                    for (int i = 0; i < b.Length; i++)
                        res[i] = i % 2 == 0 ? b[i] : b[i] * t;
                    return res;
                }
            case (not null, null): // self is dashed, other is line
                {
                    // lerp odd ones from them to 0
                    double[] res = new double[a.Length];
                    for (int i = 0; i < a.Length; i++)
                        res[i] = i % 2 == 0 ? a[i] : a[i] * (1 - t);
                    return res;
                }
        }
        // Both non-null
        int lenA = a!.Length;
        int lenB = b!.Length;
        if (lenA == 0 && lenB == 0) return null;
        int lcm = Extensions.LCM(lenA, lenB);
        double[] result = new double[lcm];
        for (int i = 0; i < lcm; i++)
            result[i] = a[i % lenA].Lerp(b[i % lenB], t);
        return result;
    }
}
