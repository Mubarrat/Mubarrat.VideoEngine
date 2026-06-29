using System.Collections;
using System.Runtime.CompilerServices;
using static Mubarrat.VideoEngine.Path.PathContour;

namespace Mubarrat.VideoEngine.Path;

public sealed class Path2D : IReadOnlyCollection<PathContour>, ILerpable<Path2D>
{
    public static readonly Path2D Empty = new(FillRule.NonZero, []);

    private readonly PathContour[] _contours;

    public FillRule FillRule { get; }

    public int Count => _contours.Length;

    public Rect Bounds { get; }

    public Path2D(FillRule fillRule, IReadOnlyList<PathContour> contours)
    {
        ArgumentNullException.ThrowIfNull(contours);

        FillRule = fillRule;
        _contours = new PathContour[contours.Count];

        for (int i = 0; i < contours.Count; i++)
            _contours[i] = contours[i] ?? throw new ArgumentException("Contour cannot be null.");

        Bounds = Rect.Union(Array.ConvertAll(_contours, x => x.Bounds));
    }

    public PathContour this[int index] => _contours[index];

    public IEnumerator<PathContour> GetEnumerator() => ((IEnumerable<PathContour>)_contours).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _contours.GetEnumerator();

    public Path2D Lerp(in Path2D other, double t) => t switch
    {
        0 => this,
        1 => other,
        _ => PathMorph.Prepare(this, other).Evaluate(t)
    };

    public static implicit operator ReadOnlySpan<PathContour>(Path2D path) => path._contours;

    public static Path2D operator *(Path2D path, Matrix2D matrix) => new(path.FillRule, Array.ConvertAll(path._contours, c => c * matrix));
    public static Path2D operator /(Path2D path, Matrix2D matrix) => path * matrix.Inverse;


    // ─────────────────────────────────────────────────────────────────────────────
    // PathMorph
    //
    // Professional topological interpolation between two Path2D objects.
    //
    // Pipeline (runs once in Prepare, not per frame):
    //
    //   1. Centroid computation  — one pass per contour, O(segments)
    //   2. Contour matching      — greedy nearest-centroid, O(n·m) where n,m are
    //                              contour counts (typically 1–10, so effectively O(1))
    //   3. Count padding         — unmatched contours from the longer path get a
    //                              point-collapsed "ghost" partner on the shorter side,
    //                              so the ghost grows/shrinks to/from a point during lerp
    //   4. Per-pair ContourMorph — delegates to the existing O(n) ContourMorph.Prepare
    //
    // Per-frame Evaluate(t) calls ContourMorph.Evaluate on each pair — O(total segments).
    // ─────────────────────────────────────────────────────────────────────────────
    internal sealed class PathMorph
    {
        private readonly ContourMorph[] _morphs;
        private readonly FillRule _fillRuleA;
        private readonly FillRule _fillRuleB;

        private PathMorph(ContourMorph[] morphs, FillRule fillRuleA, FillRule fillRuleB)
        {
            _morphs = morphs;
            _fillRuleA = fillRuleA;
            _fillRuleB = fillRuleB;
        }

        // ── Public factory ───────────────────────────────────────────────────────

        /// <summary>
        /// Prepares a reusable morph between two paths.
        /// Call once per transition; then call <see cref="Evaluate"/> every frame.
        /// </summary>
        public static PathMorph Prepare(Path2D from, Path2D to)
        {
            ArgumentNullException.ThrowIfNull(from);
            ArgumentNullException.ThrowIfNull(to);

            // 1. Compute centroids for every contour in both paths
            Point[] centroidsA = Array.ConvertAll(from._contours, Centroid);
            Point[] centroidsB = Array.ConvertAll(to._contours, Centroid);

            int na = from.Count, nb = to.Count;

            // 2. Greedy contour matching: for each A[i] find the nearest unmatched B[j].
            //    O(na · nb) — contour counts are tiny (1–~10) so this is negligible.
            //    matchAtoB[i] = j means A[i] is paired with B[j].
            //    Unmatched B contours (when nb > na) are recorded in unmatchedB.
            int[] matchAtoB = new int[na];
            bool[] usedB = new bool[nb];
            List<int> unmatchedB = new(Math.Max(0, nb - na));

            for (int i = 0; i < na; i++)
            {
                int best = -1;
                double bestD = double.MaxValue;
                for (int j = 0; j < nb; j++)
                {
                    if (usedB[j]) continue;
                    double d = Dist2(centroidsA[i], centroidsB[j]);
                    if (d < bestD) { bestD = d; best = j; }
                }

                if (best == -1)
                {
                    // nb < na: no B contour left — collapse A[i] to its own centroid.
                    // We handle this below by pairing with a ghost.
                    matchAtoB[i] = -1;
                }
                else
                {
                    matchAtoB[i] = best;
                    usedB[best] = true;
                }
            }

            // Collect unmatched B indices (when nb > na)
            for (int j = 0; j < nb; j++)
                if (!usedB[j]) unmatchedB.Add(j);

            // 3. Build the ContourMorph array.
            //    Pairs:   matched A[i] ↔ B[matchAtoB[i]]
            //    Extras:  unmatched B[j] ↔ ghost(A side) collapsed to centroid of B[j]
            //             unmatched A[i] ↔ ghost(B side) collapsed to centroid of A[i]
            int totalPairs = na + unmatchedB.Count;
            var morphs = new ContourMorph[totalPairs];

            // Matched pairs
            for (int i = 0; i < na; i++)
            {
                PathContour ca = from[i];
                PathContour cb = matchAtoB[i] == -1
                    ? CollapseToPoint(ca, centroidsA[i])   // nb < na: B ghost
                    : to[matchAtoB[i]];
                morphs[i] = ContourMorph.Prepare(ca, cb);
            }

            // Extra B contours that had no A partner (nb > na): grow from a point
            for (int k = 0; k < unmatchedB.Count; k++)
            {
                PathContour cb = to[unmatchedB[k]];
                PathContour ghost = CollapseToPoint(cb, centroidsB[unmatchedB[k]]);
                morphs[na + k] = ContourMorph.Prepare(ghost, cb);
            }

            return new PathMorph(morphs, from.FillRule, to.FillRule);
        }

        // ── Per-frame evaluate ───────────────────────────────────────────────────

        /// <summary>
        /// Interpolates between the two paths.  O(total segments) — safe every frame.
        /// Fill rule switches at t = 0.5 (fill rules are discrete, cannot be lerped).
        /// </summary>
        public Path2D Evaluate(double t)
        {
            FillRule rule = t < 0.5 ? _fillRuleA : _fillRuleB;

            var contours = new PathContour[_morphs.Length];
            for (int i = 0; i < _morphs.Length; i++)
                contours[i] = _morphs[i].Evaluate(t);

            return new Path2D(rule, contours);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Computes the centroid of a contour as the average of all segment start-points.
        /// One pass, O(n segments).
        /// </summary>
        private static Point Centroid(PathContour contour)
        {
            if (contour.Count == 0) return default;

            double sx = 0, sy = 0;
            int n = 0;
            foreach (var seg in contour)
            {
                sx += seg.Start.X;
                sy += seg.Start.Y;
                n++;
            }
            return new Point(sx / n, sy / n);
        }

        /// <summary>
        /// Creates a degenerate PathContour with the same topology as <paramref name="template"/>
        /// but with all control points collapsed to <paramref name="point"/>.
        /// When lerped against <paramref name="template"/>, this contour grows from
        /// (or shrinks to) a single point, giving a clean appear/disappear animation.
        /// </summary>
        private static PathContour CollapseToPoint(PathContour template, Point point)
        {
            var segs = new IPathSegment[template.Count];
            for (int i = 0; i < template.Count; i++)
                segs[i] = new LineSegment(point, point);
            return new PathContour(segs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Dist2(Point a, Point b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }
}
