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
    //   1. Geometry-key build    — flatten contours once and classify nesting depth
    //   2. Contour matching      — role-aware assignment, matching holes with holes
    //                              and outer contours with outer contours
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

            int na = from.Count, nb = to.Count;

            // 1. Sort both sides by the same stable geometry key. The key starts
            //    with containment depth so nested counters cannot swap with their
            //    outer contour when their visual centers are close.
            ContourKey[] fromKeys = BuildContourKeys(from._contours, from.FillRule);
            ContourKey[] toKeys = BuildContourKeys(to._contours, to.FillRule);
            Array.Sort(fromKeys, CompareContourKey);
            Array.Sort(toKeys, CompareContourKey);

            // 2. Build the ContourMorph array.
            //    Pairs:   role-aware A[i] ↔ B[j]
            //    Extras:  unmatched contours grow/shrink from their own center
            int[] matchFromTo = MatchContourKeys(fromKeys, toKeys);
            bool[] usedTo = new bool[nb];
            var morphs = new List<ContourMorph>(Math.Max(na, nb));

            for (int i = 0; i < na; i++)
            {
                PathContour ca = from[fromKeys[i].Index];
                int matchedTo = matchFromTo[i];

                if (matchedTo >= 0)
                {
                    usedTo[matchedTo] = true;
                    morphs.Add(ContourMorph.Prepare(ca, to[toKeys[matchedTo].Index]));
                }
                else
                {
                    morphs.Add(ContourMorph.Prepare(ca, CollapseToPoint(ca, fromKeys[i].Center)));
                }
            }

            for (int i = 0; i < nb; i++)
            {
                if (usedTo[i])
                    continue;

                PathContour cb = to[toKeys[i].Index];
                morphs.Add(ContourMorph.Prepare(CollapseToPoint(cb, toKeys[i].Center), cb));
            }

            return new PathMorph([.. morphs], from.FillRule, to.FillRule);
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
        /// Computes the center point of a contour as the average of all segment mid-points.
        /// One pass, O(n segments).
        /// </summary>
        private static Point CenterPoint(PathContour contour)
        {
            if (contour.Count == 0) return default;

            double sx = 0, sy = 0;
            int n = 0;
            foreach (var seg in contour)
            {
                sx += (seg.Start.X + seg.End.X) * 0.5;
                sy += (seg.Start.Y + seg.End.Y) * 0.5;
                n++;
            }
            return new Point(sx / n, sy / n);
        }

        private static ContourKey[] BuildContourKeys(PathContour[] contours, FillRule fillRule)
        {
            var outlines = new ContourOutline[contours.Length];
            var keys = new ContourKey[contours.Length];

            for (int i = 0; i < contours.Length; i++)
            {
                PathContour contour = contours[i];
                Rect bounds = contour.Bounds.Normalized;
                Point center = CenterPoint(contour);
                Point[] points = FlattenContour(contour);
                double signedArea = SignedArea(points);
                double boundsArea = bounds.IsNaN ? 0 : Math.Abs(bounds.Size.Area);

                outlines[i] = new ContourOutline(points, bounds, center);
                keys[i] = new ContourKey(i, 0, 0, center, signedArea, boundsArea);
            }

            for (int i = 0; i < keys.Length; i++)
            {
                int depth = 0;

                for (int j = 0; j < outlines.Length; j++)
                {
                    if (i == j)
                        continue;

                    if (IsNestedInside(outlines[i], outlines[j]))
                        depth++;
                }

                keys[i] = keys[i] with { Depth = depth };
            }

            int outerSign = FindOuterWindingSign(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                int role = keys[i].Depth & 1;

                if (fillRule == FillRule.NonZero && outerSign != 0)
                {
                    int sign = Math.Sign(keys[i].SignedArea);
                    if (sign != 0 && sign != outerSign)
                        role = 1;
                }

                keys[i] = keys[i] with { Role = role };
            }

            return keys;
        }

        private static int CompareContourKey(ContourKey a, ContourKey b)
        {
            int cmp = a.Role.CompareTo(b.Role);
            if (cmp != 0) return cmp;

            cmp = a.Depth.CompareTo(b.Depth);
            if (cmp != 0) return cmp;

            cmp = a.Center.X.CompareTo(b.Center.X);
            if (cmp != 0) return cmp;

            cmp = a.Center.Y.CompareTo(b.Center.Y);
            if (cmp != 0) return cmp;

            cmp = a.BoundsArea.CompareTo(b.BoundsArea);
            if (cmp != 0) return cmp;

            cmp = Math.Abs(b.SignedArea).CompareTo(Math.Abs(a.SignedArea));
            if (cmp != 0) return cmp;

            return a.Index.CompareTo(b.Index);
        }

        private static int[] MatchContourKeys(ContourKey[] fromKeys, ContourKey[] toKeys)
        {
            var result = new int[fromKeys.Length];
            Array.Fill(result, -1);

            if (toKeys.Length == 0)
                return result;

            var used = new bool[toKeys.Length];

            for (int i = 0; i < fromKeys.Length; i++)
            {
                int best = -1;
                double bestCost = double.PositiveInfinity;

                for (int j = 0; j < toKeys.Length; j++)
                {
                    if (used[j])
                        continue;

                    double cost = PairCost(fromKeys[i], toKeys[j]);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        best = j;
                    }
                }

                if (best >= 0)
                {
                    used[best] = true;
                    result[i] = best;
                }
            }

            return result;
        }

        private static double PairCost(ContourKey a, ContourKey b)
        {
            double cost = 0;

            if (a.Role != b.Role)
                cost += 1_000_000_000_000.0;

            cost += Math.Abs(a.Depth - b.Depth) * 1_000_000_000.0;

            int signA = Math.Sign(a.SignedArea);
            int signB = Math.Sign(b.SignedArea);
            if (signA != 0 && signB != 0 && signA != signB)
                cost += 100_000_000.0;

            double areaA = Math.Max(Math.Abs(a.SignedArea), a.BoundsArea);
            double areaB = Math.Max(Math.Abs(b.SignedArea), b.BoundsArea);
            double areaScale = Math.Max(Math.Max(areaA, areaB), 1.0);

            cost += Dist2(a.Center, b.Center) / areaScale;
            cost += Math.Abs(Math.Log((areaA + 1.0) / (areaB + 1.0))) * 10.0;

            return cost;
        }

        private static int FindOuterWindingSign(ContourKey[] keys)
        {
            int sign = 0;
            double bestArea = 0;

            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].Depth != 0)
                    continue;

                double area = Math.Abs(keys[i].SignedArea);
                if (area > bestArea)
                {
                    bestArea = area;
                    sign = Math.Sign(keys[i].SignedArea);
                }
            }

            if (sign != 0)
                return sign;

            for (int i = 0; i < keys.Length; i++)
            {
                double area = Math.Abs(keys[i].SignedArea);
                if (area > bestArea)
                {
                    bestArea = area;
                    sign = Math.Sign(keys[i].SignedArea);
                }
            }

            return sign;
        }

        private static Point[] FlattenContour(PathContour contour)
        {
            if (contour.Count == 0)
                return [];

            var points = new List<Point>(Math.Max(8, contour.Count * 4));

            foreach (var segment in contour)
            {
                if (points.Count == 0 || points[^1] != segment.Start)
                    points.Add(segment.Start);

                switch (segment)
                {
                    case LineSegment line:
                        points.Add(line.End);
                        break;
                    case QuadraticSegment quadratic:
                        AddQuadraticPoints(points, quadratic);
                        break;
                    case CubicSegment cubic:
                        AddCubicPoints(points, cubic);
                        break;
                    default:
                        points.Add(segment.End);
                        break;
                }
            }

            if (points.Count > 1 && points[^1] == points[0])
                points.RemoveAt(points.Count - 1);

            return [.. points];
        }

        private static void AddQuadraticPoints(List<Point> points, QuadraticSegment segment)
        {
            int steps = EstimateSteps(segment.Start, segment.Control, segment.End);
            for (int step = 1; step <= steps; step++)
            {
                double t = step / (double)steps;
                points.Add(EvaluateQuadratic(segment, t));
            }
        }

        private static void AddCubicPoints(List<Point> points, CubicSegment segment)
        {
            int steps = EstimateSteps(segment.Start, segment.Control1, segment.Control2, segment.End);
            for (int step = 1; step <= steps; step++)
            {
                double t = step / (double)steps;
                points.Add(EvaluateCubic(segment, t));
            }
        }

        private static int EstimateSteps(params ReadOnlySpan<Point> points)
        {
            double polygonLength = 0;
            for (int i = 1; i < points.Length; i++)
                polygonLength += Dist(points[i - 1], points[i]);

            return Math.Clamp((int)Math.Ceiling(polygonLength / 24.0), 4, 24);
        }

        private static Point EvaluateQuadratic(QuadraticSegment segment, double t)
        {
            double mt = 1 - t;
            double a = mt * mt;
            double b = 2 * mt * t;
            double c = t * t;
            return new(
                a * segment.Start.X + b * segment.Control.X + c * segment.End.X,
                a * segment.Start.Y + b * segment.Control.Y + c * segment.End.Y);
        }

        private static Point EvaluateCubic(CubicSegment segment, double t)
        {
            double mt = 1 - t;
            double a = mt * mt * mt;
            double b = 3 * mt * mt * t;
            double c = 3 * mt * t * t;
            double d = t * t * t;
            return new(
                a * segment.Start.X + b * segment.Control1.X + c * segment.Control2.X + d * segment.End.X,
                a * segment.Start.Y + b * segment.Control1.Y + c * segment.Control2.Y + d * segment.End.Y);
        }

        private static double SignedArea(Point[] points)
        {
            if (points.Length < 3)
                return 0;

            double area = 0;
            for (int i = 0; i < points.Length; i++)
            {
                Point current = points[i];
                Point next = points[(i + 1) % points.Length];
                area += current.X * next.Y - current.Y * next.X;
            }
            return area * 0.5;
        }

        private static bool IsNestedInside(ContourOutline candidate, ContourOutline container)
        {
            if (candidate.Points.Length < 3 || container.Points.Length < 3)
                return false;

            if (!BoundsContains(container.Bounds, candidate.Bounds))
                return false;

            int samples = 0;
            int inside = 0;
            int stride = Math.Max(1, candidate.Points.Length / 24);

            if (ContainsPoint(container, candidate.Center))
                inside++;
            samples++;

            for (int i = 0; i < candidate.Points.Length; i += stride)
            {
                if (ContainsPoint(container, candidate.Points[i]))
                    inside++;
                samples++;
            }

            return inside * 2 >= samples;
        }

        private static bool BoundsContains(Rect outer, Rect inner)
        {
            const double epsilon = 1e-9;
            return !outer.IsNaN &&
                !inner.IsNaN &&
                inner.Left >= outer.Left - epsilon &&
                inner.Right <= outer.Right + epsilon &&
                inner.Top >= outer.Top - epsilon &&
                inner.Bottom <= outer.Bottom + epsilon;
        }

        private static bool ContainsPoint(ContourOutline outline, Point point)
        {
            if (outline.Points.Length < 3)
                return false;

            Rect bounds = outline.Bounds;
            const double epsilon = 1e-9;
            if (bounds.IsNaN ||
                point.X < bounds.Left - epsilon ||
                point.X > bounds.Right + epsilon ||
                point.Y < bounds.Top - epsilon ||
                point.Y > bounds.Bottom + epsilon)
                return false;

            bool inside = false;
            Point previous = outline.Points[^1];

            for (int i = 0; i < outline.Points.Length; i++)
            {
                Point current = outline.Points[i];
                bool crosses = (current.Y > point.Y) != (previous.Y > point.Y);

                if (crosses)
                {
                    double x = (previous.X - current.X) * (point.Y - current.Y) /
                        (previous.Y - current.Y) + current.X;
                    if (x > point.X)
                        inside = !inside;
                }

                previous = current;
            }

            return inside;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Dist(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Dist2(Point a, Point b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
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

        private readonly record struct ContourOutline(Point[] Points, Rect Bounds, Point Center);

        private readonly record struct ContourKey(
            int Index,
            int Depth,
            int Role,
            Point Center,
            double SignedArea,
            double BoundsArea);
    }
}
