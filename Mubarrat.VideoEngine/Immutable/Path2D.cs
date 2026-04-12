using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Immutable;

[StructLayout(LayoutKind.Sequential)]
public struct Path2D(bool IsNonZeroFill, params Subpath[] subpaths) : ILerpable<Path2D>
{
    public bool IsNonZeroFill = IsNonZeroFill;
    public Subpath[] Subpaths = subpaths ?? [];

    public readonly Path2D Lerp(in Path2D target, double t)
    {
        if (t == 0) return this;
        if (t == 1) return target;

        bool fill = t < 0.5 ? IsNonZeroFill : target.IsNonZeroFill;

        switch (Subpaths.Length, target.Subpaths.Length)
        {
            case (0, 0): return new Path2D(fill);
            case (0, _): return new Path2D(fill, new Subpath([])).Lerp(target, t);
            case (_, 0): return Lerp(new Path2D(fill, new Subpath([])), t);
        }

        var used = new bool[target.Subpaths.Length];
        var result = new List<Subpath>(Math.Max(Subpaths.Length, target.Subpaths.Length));

        foreach (var a in Subpaths)
        {
            int best = -1;
            double bestScore = double.MaxValue;

            for (int i = 0; i < target.Subpaths.Length; i++)
            {
                if (used[i]) continue;

                double score =
                    a.CenterPoint.DistanceTo(target.Subpaths[i].CenterPoint) +
                    Math.Abs(a.Bounds.Size.Area - target.Subpaths[i].Bounds.Size.Area);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            if (best != -1)
            {
                used[best] = true;
                result.Add(a.Lerp(target.Subpaths[best], t));
            }
            else
            {
                result.Add(a.Lerp(new Subpath([]), t));
            }
        }

        // remaining targets
        for (int i = 0; i < target.Subpaths.Length; i++)
        {
            if (!used[i])
                result.Add(new Subpath([]).Lerp(target.Subpaths[i], t));
        }

        return new Path2D(fill, result.ToArray());
    }

    public readonly Point CenterPoint
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Subpaths.Select(sp => sp.CenterPoint).ToArray().Average();
    }

    public readonly Rect Bounds => Subpaths.Select(x => x.Bounds).Aggregate((a, b) => a.Union(b));
}
