using System.Collections;
using System.Runtime.CompilerServices;

namespace Mubarrat.VideoEngine.Path;

public sealed class PathContour : IReadOnlyCollection<IPathSegment>, ILerpable<PathContour>
{
    private readonly IPathSegment[] _segments;

    public bool IsClosed { get; }

    public int Count => _segments.Length;

    public Rect Bounds { get; }

    public PathContour(IReadOnlyList<IPathSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        _segments = new IPathSegment[segments.Count];

        for (int i = 0; i < segments.Count; i++)
            _segments[i] = segments[i] ?? throw new ArgumentException("Segment cannot be null.");

        IsClosed = _segments.Length > 0 && _segments[0].Start == _segments[^1].End;

        Bounds = Rect.Union(Array.ConvertAll(_segments, x => x.Bounds));
    }

    public IEnumerator<IPathSegment> GetEnumerator() => ((IEnumerable<IPathSegment>)_segments).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _segments.GetEnumerator();

    public PathContour Lerp(in PathContour other, double t) => t switch
    {
        0 => this,
        1 => other,
        _ => ContourMorph.Prepare(this, other).Evaluate(t)
    };

    public IPathSegment this[int index] => _segments[index];

    public static implicit operator ReadOnlySpan<IPathSegment>(PathContour contour) => contour._segments;

    public static PathContour operator *(PathContour contour, Matrix2D matrix) => new(Array.ConvertAll(contour._segments, s => s * matrix));
    public static PathContour operator /(PathContour contour, Matrix2D matrix) => contour * matrix.Inverse;

    // ─────────────────────────────────────────────────────────────────────────────
    // ContourMorph
    //
    // Professional topological interpolation between two PathContours.
    //
    // Pipeline (runs once in PrepareMorph, not per frame):
    //
    //   1. Degree elevation  — every segment → CubicSegment (exact, lossless)
    //   2. Resampling        — bring both arrays to the same count by splitting
    //                          the longest segment (by Gravesen arc-length approx)
    //                          in the shorter array, repeatedly, until counts match
    //   3. Phase alignment   — for closed contours, find the cyclic rotation k
    //                          that minimises Σ dist(A[i].Start, B[(i+k)%n].Start)
    //                          also tries reversing B to handle winding mismatches
    //
    // Per-frame Evaluate(t) is then just n linear interpolations of 4 points each.
    // ─────────────────────────────────────────────────────────────────────────────
    internal sealed class ContourMorph
    {
        // Normalised, resampled, phase-aligned cubic arrays.
        // _a[i] lerps to _b[i] at t=1.
        private readonly CubicSegment[] _a;
        private readonly CubicSegment[] _b;

        private ContourMorph(CubicSegment[] a, CubicSegment[] b)
        {
            _a = a;
            _b = b;
        }

        // ── Public factory ───────────────────────────────────────────────────────

        /// <summary>
        /// Prepares a morph between two contours.
        /// This is the expensive step — call once, then call <see cref="Evaluate"/> per frame.
        /// </summary>
        /// <param name="from">Source contour (t = 0).</param>
        /// <param name="to">Target contour (t = 1).</param>
        public static ContourMorph Prepare(PathContour from, PathContour to)
        {
            ArgumentNullException.ThrowIfNull(from);
            ArgumentNullException.ThrowIfNull(to);

            // 1. Degree elevation: everything → CubicSegment
            var a = ElevateToCubic(from);
            var b = ElevateToCubic(to);

            // 2. Resampling: bring both to max(|a|, |b|) by splitting
            int target = Math.Max(a.Count, b.Count);
            Resample(a, target);
            Resample(b, target);

            // 3. Phase alignment (closed contours only)
            bool closed = from.IsClosed || to.IsClosed;
            if (closed && a.Count > 1)
                AlignPhase(a, b);

            return new ContourMorph([.. a], [.. b]);
        }

        // ── Per-frame evaluate ───────────────────────────────────────────────────

        /// <summary>
        /// Interpolates between the two contours.  O(n) — safe to call every frame.
        /// </summary>
        /// <param name="t">Interpolation factor, typically [0, 1].</param>
        public PathContour Evaluate(double t)
        {
            if (t <= 0) return BuildContour(_a);
            if (t >= 1) return BuildContour(_b);

            var segs = new IPathSegment[_a.Length];
            for (int i = 0; i < _a.Length; i++)
            {
                ref readonly var sa = ref _a[i];
                ref readonly var sb = ref _b[i];
                segs[i] = new CubicSegment(
                    sa.Start.Lerp(sb.Start, t),
                    sa.Control1.Lerp(sb.Control1, t),
                    sa.Control2.Lerp(sb.Control2, t),
                    sa.End.Lerp(sb.End, t));
            }
            return new PathContour(segs);
        }

        // ── Step 1: Degree elevation ─────────────────────────────────────────────

        private static List<CubicSegment> ElevateToCubic(PathContour contour)
        {
            var result = new List<CubicSegment>(contour.Count);
            foreach (var seg in contour)
            {
                result.Add(seg switch
                {
                    CubicSegment c => c,
                    QuadraticSegment q => QuadToCubic(q),
                    LineSegment l => LineToCubic(l),
                    _ => throw new NotSupportedException(
                                              $"Cannot elevate segment type {seg.GetType().Name}")
                });
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static CubicSegment LineToCubic(LineSegment l)
        {
            // Exact degree elevation: split the chord at 1/3 and 2/3
            Point c1 = l.Start.Lerp(l.End, 1.0 / 3.0);
            Point c2 = l.Start.Lerp(l.End, 2.0 / 3.0);
            return new CubicSegment(l.Start, c1, c2, l.End);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static CubicSegment QuadToCubic(QuadraticSegment q)
        {
            // Exact degree elevation formula (no approximation):
            //   c1 = p0 + 2/3 * (p1 - p0)
            //   c2 = p2 + 2/3 * (p1 - p2)
            Point c1 = q.Start.Lerp(q.Control, 2.0 / 3.0);
            Point c2 = q.End.Lerp(q.Control, 2.0 / 3.0);
            return new CubicSegment(q.Start, c1, c2, q.End);
        }

        // ── Step 2: Resampling ───────────────────────────────────────────────────

        /// <summary>
        /// Grows <paramref name="segs"/> to <paramref name="targetCount"/> by
        /// repeatedly splitting the segment with the largest Gravesen arc-length
        /// approximation. Each split inserts one segment (de Casteljau at t=0.5),
        /// so we call this (targetCount - segs.Count) times.
        /// </summary>
        private static void Resample(List<CubicSegment> segs, int targetCount)
        {
            while (segs.Count < targetCount)
            {
                // Find the segment with the highest arc-length estimate
                int best = 0;
                double bestLen = -1;
                for (int i = 0; i < segs.Count; i++)
                {
                    double len = GravesenLength(segs[i]);
                    if (len > bestLen) { bestLen = len; best = i; }
                }

                // Split it at t = 0.5 using de Casteljau
                SplitCubic(segs[best], 0.5, out var left, out var right);
                segs.RemoveAt(best);
                segs.Insert(best, right);
                segs.Insert(best, left);
            }
        }

        /// <summary>
        /// Gravesen's arc-length approximation for a cubic Bézier:
        ///   L ≈ (chord + control_polygon) / 2
        /// Fast, cheap, good enough for choosing which segment to split.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double GravesenLength(in CubicSegment c)
        {
            double chord = Dist(c.Start, c.End);
            double poly = Dist(c.Start, c.Control1)
                         + Dist(c.Control1, c.Control2)
                         + Dist(c.Control2, c.End);
            return (chord + poly) * 0.5;
        }

        /// <summary>
        /// De Casteljau subdivision of a cubic Bézier at parameter <paramref name="t"/>.
        /// Produces two cubics that together exactly reproduce the original.
        /// </summary>
        private static void SplitCubic(in CubicSegment c, double t,
            out CubicSegment left, out CubicSegment right)
        {
            // Level 1
            Point p01 = c.Start.Lerp(c.Control1, t);
            Point p12 = c.Control1.Lerp(c.Control2, t);
            Point p23 = c.Control2.Lerp(c.End, t);
            // Level 2
            Point p012 = p01.Lerp(p12, t);
            Point p123 = p12.Lerp(p23, t);
            // Level 3
            Point p0123 = p012.Lerp(p123, t);

            left = new CubicSegment(c.Start, p01, p012, p0123);
            right = new CubicSegment(p0123, p123, p23, c.End);
        }

        // ── Step 3: Phase alignment ──────────────────────────────────────────────

        /// <summary>
        /// Winding-safe phase alignment.
        ///
        /// Step A — reverse only when winding differs. Reversing merely because
        ///           a reversed contour scores lower can mirror symmetric glyphs
        ///           and swap hole/outer semantics during interpolation.
        ///
        /// Step B — rotate B to the best cyclic start index using the same
        ///           curvature-weighted matcher used by the old Immutable.Subpath
        ///           implementation, but directly on the cubic contour vertices.
        /// </summary>
        private static void AlignPhase(List<CubicSegment> a, List<CubicSegment> b)
        {
            int n = a.Count; // == b.Count after resampling
            if (n == 0 || b.Count != n)
                return;

            double areaA = SignedArea(a);
            double areaB = SignedArea(b);
            const double AreaEpsilon = 1e-12;

            if (Math.Abs(areaA) > AreaEpsilon &&
                Math.Abs(areaB) > AreaEpsilon &&
                Math.Sign(areaA) != Math.Sign(areaB))
                ReverseCubicList(b);

            Point[] reference = BuildPhasePoints(a);
            Point[] candidate = BuildPhasePoints(b);
            int shift = FindBestCyclicShift(reference, candidate);
            if (shift != 0) RotateCubicList(b, shift);
        }

        private static double SignedArea(List<CubicSegment> segs)
        {
            double area = 0;
            int n = segs.Count;

            for (int i = 0; i < n; i++)
            {
                Point p = segs[i].Start;
                Point q = segs[(i + 1) % n].Start;
                area += p.X * q.Y - p.Y * q.X;
            }

            return area;
        }

        private static Point[] BuildPhasePoints(List<CubicSegment> segs)
        {
            var points = new Point[segs.Count];
            if (segs.Count == 0)
                return points;

            double cx = 0;
            double cy = 0;

            for (int i = 0; i < segs.Count; i++)
            {
                Point p = segs[i].Start;
                cx += p.X;
                cy += p.Y;
            }

            cx /= segs.Count;
            cy /= segs.Count;

            double scale2 = 0;
            for (int i = 0; i < segs.Count; i++)
            {
                Point p = segs[i].Start;
                double dx = p.X - cx;
                double dy = p.Y - cy;
                scale2 += dx * dx + dy * dy;
            }

            double scale = scale2 > double.Epsilon
                ? Math.Sqrt(scale2 / segs.Count)
                : 1;

            for (int i = 0; i < segs.Count; i++)
            {
                Point p = segs[i].Start;
                points[i] = new Point((p.X - cx) / scale, (p.Y - cy) / scale);
            }

            return points;
        }

        private static int FindBestCyclicShift(Point[] reference, Point[] candidate)
        {
            int n = reference.Length;
            if (n == 0 || candidate.Length != n)
                return 0;

            if (n <= 12)
                return FindBestCyclicShiftExhaustive(reference, candidate);

            double[] referenceCurvature = BuildCurvatureWeights(reference);
            double[] candidateCurvature = BuildCurvatureWeights(candidate);

            int coarseStride = Math.Max(1, n >> 4);
            int sampleStep = Math.Max(1, n >> 4);

            int coarseBestShift = 0;
            double coarseBestScore = double.MaxValue;

            for (int shift = 0; shift < n; shift += coarseStride)
            {
                double score = EvaluateShiftScore(reference, candidate, referenceCurvature, candidateCurvature, shift, sampleStep, coarseBestScore);
                if (score < coarseBestScore)
                {
                    coarseBestScore = score;
                    coarseBestShift = shift;
                }
            }

            int refineRadius = Math.Max(3, coarseStride);
            int bestShift = coarseBestShift;
            double bestScore = double.MaxValue;

            for (int delta = -refineRadius; delta <= refineRadius; delta++)
            {
                int shift = Mod(coarseBestShift + delta, n);
                double score = EvaluateShiftScore(reference, candidate, referenceCurvature, candidateCurvature, shift, 1, bestScore);
                if (score < bestScore)
                {
                    bestScore = score;
                    bestShift = shift;
                }
            }

            double zeroScore = EvaluateShiftScore(reference, candidate, referenceCurvature, candidateCurvature, 0, 1, bestScore);
            if (zeroScore <= bestScore * 1.000000001d)
                return 0;

            return bestShift;
        }

        private static int FindBestCyclicShiftExhaustive(Point[] reference, Point[] candidate)
        {
            int n = reference.Length;
            int bestShift = 0;
            double bestScore = double.MaxValue;

            for (int shift = 0; shift < n; shift++)
            {
                double score = 0;
                for (int i = 0; i < n; i++)
                {
                    Point a = reference[i];
                    Point b = candidate[(i + shift) % n];

                    score += Dist2(a, b);
                    if (score >= bestScore)
                        break;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestShift = shift;
                }
            }

            return bestShift;
        }

        private static double[] BuildCurvatureWeights(Point[] points)
        {
            int n = points.Length;
            var curvature = new double[n];

            if (n < 3)
                return curvature;

            for (int i = 0; i < n; i++)
            {
                Point prev = points[(i - 1 + n) % n];
                Point cur = points[i];
                Point next = points[(i + 1) % n];

                double ax = cur.X - prev.X;
                double ay = cur.Y - prev.Y;
                double bx = next.X - cur.X;
                double by = next.Y - cur.Y;

                double al2 = ax * ax + ay * ay;
                double bl2 = bx * bx + by * by;

                if (al2 <= double.Epsilon || bl2 <= double.Epsilon)
                    continue;

                double cross = Math.Abs(ax * by - ay * bx);
                curvature[i] = cross / Math.Sqrt(al2 * bl2);
            }

            return curvature;
        }

        private static double EvaluateShiftScore(
            Point[] reference,
            Point[] candidate,
            double[] referenceCurvature,
            double[] candidateCurvature,
            int shift,
            int step,
            double earlyExit)
        {
            const double CurvatureMatchWeight = 0.45;
            const double CurvatureMismatchPenalty = 0.20;

            int n = reference.Length;
            double score = 0;

            for (int i = 0; i < n; i += step)
            {
                int j = i + shift;
                if (j >= n) j -= n;

                Point a = reference[i];
                Point b = candidate[j];

                double dist2 = Dist2(a, b);
                double ka = referenceCurvature[i];
                double kb = candidateCurvature[j];
                double boost = 1d + CurvatureMatchWeight * (ka + kb);
                double mismatch = ka - kb;

                score += dist2 * boost + CurvatureMismatchPenalty * mismatch * mismatch;

                if (score >= earlyExit)
                    return score;
            }

            return score;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Mod(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        /// <summary>
        /// Reverses the list and flips each segment so Start↔End and controls swap,
        /// keeping the curve geometrically identical but wound the other way.
        /// </summary>
        private static void ReverseCubicList(List<CubicSegment> segs)
        {
            segs.Reverse();
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                // Reversing a cubic: swap endpoints, swap control points
                segs[i] = new CubicSegment(s.End, s.Control2, s.Control1, s.Start);
            }
        }

        /// <summary>
        /// Rotates the list so that element at index <paramref name="offset"/>
        /// becomes element 0.
        /// </summary>
        private static void RotateCubicList(List<CubicSegment> segs, int offset)
        {
            if (offset == 0) return;
            // CollectionsMarshal-free rotation via three-reverse trick
            segs.Reverse(0, offset);
            segs.Reverse(offset, segs.Count - offset);
            segs.Reverse();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Dist(Point a, Point b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Dist2(Point a, Point b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private static PathContour BuildContour(CubicSegment[] segs)
            => new(segs.Cast<IPathSegment>().ToArray());
    }
}
