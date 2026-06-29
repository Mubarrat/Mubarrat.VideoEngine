using System.Runtime.CompilerServices;

namespace Mubarrat.VideoEngine.Path;

// ─────────────────────────────────────────────────────────────────────────────
// Kernel math helpers — all static, all aggressively inlined
// These are called from IL-generated delegates; keeping them as named statics
// means the JIT can inline them into the generated code path.
// ─────────────────────────────────────────────────────────────────────────────
public static class KernelMath
{
    // ── Rect distance (lower-bound for kernel distance) ──────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double RectDist(double px, double py,
        double rx, double ry, double rw, double rh)
    {
        double dx = Math.Max(0, Math.Max(rx - px, px - (rx + rw)));
        double dy = Math.Max(0, Math.Max(ry - py, py - (ry + rh)));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // ── Line ─────────────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double LineDistance(
        double px, double py,
        double ax, double ay, double bx, double by)
    {
        double abx = bx - ax, aby = by - ay;
        double apx = px - ax, apy = py - ay;
        double denom = abx * abx + aby * aby;
        double t = denom < 1e-14 ? 0 : Math.Clamp((apx * abx + apy * aby) / denom, 0, 1);
        double dx = px - (ax + t * abx);
        double dy = py - (ay + t * aby);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Winding contribution of a line segment using the +X ray-cast rule.
    /// Returns +1 for upward crossing to the right of p, -1 for downward, 0 otherwise.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LineWinding(
        double px, double py,
        double ax, double ay, double bx, double by)
    {
        // Exactly one endpoint on the ray boundary: use half-open interval [ay, by)
        if (ay <= py)
        {
            if (by > py)
            {
                // upward crossing
                double t = (py - ay) / (by - ay);
                if (ax + t * (bx - ax) > px) return 1;
            }
        }
        else
        {
            if (by <= py)
            {
                // downward crossing
                double t = (py - ay) / (by - ay);
                if (ax + t * (bx - ax) > px) return -1;
            }
        }
        return 0;
    }

    // ── Quadratic Bézier ─────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (double x, double y) QuadEval(
        double t,
        double p0x, double p0y, double p1x, double p1y, double p2x, double p2y)
    {
        double u = 1 - t;
        return (u * u * p0x + 2 * u * t * p1x + t * t * p2x,
                u * u * p0y + 2 * u * t * p1y + t * t * p2y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (double dx, double dy) QuadDeriv(
        double t,
        double p0x, double p0y, double p1x, double p1y, double p2x, double p2y)
    {
        double u = 1 - t;
        return (2 * u * (p1x - p0x) + 2 * t * (p2x - p1x),
                2 * u * (p1y - p0y) + 2 * t * (p2y - p1y));
    }

    public static double QuadDistance(
        double px, double py,
        double p0x, double p0y, double p1x, double p1y, double p2x, double p2y)
    {
        double best = double.MaxValue;

        // 5 seeds to avoid local-minimum traps on inflected curves
        ReadOnlySpan<double> seeds = [0.0, 0.25, 0.5, 0.75, 1.0];
        foreach (double seed in seeds)
        {
            double t = seed;
            for (int i = 0; i < 5; i++)
            {
                var (cx, cy) = QuadEval(t, p0x, p0y, p1x, p1y, p2x, p2y);
                var (dx, dy) = QuadDeriv(t, p0x, p0y, p1x, p1y, p2x, p2y);
                double vx = cx - px, vy = cy - py;
                double f = vx * dx + vy * dy;
                double df = dx * dx + dy * dy;
                if (Math.Abs(df) < 1e-14) break;
                t -= f / df;
                t = Math.Clamp(t, 0, 1);
            }
            var (ex, ey) = QuadEval(t, p0x, p0y, p1x, p1y, p2x, p2y);
            double d = Math.Sqrt((px - ex) * (px - ex) + (py - ey) * (py - ey));
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>
    /// Solve at² + bt + c = 0 and push valid roots in (0, 1] into roots span.
    /// Returns the count of roots found.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SolveQuadratic(double a, double b, double c, Span<double> roots)
    {
        int count = 0;
        if (Math.Abs(a) < 1e-14)
        {
            // linear
            if (Math.Abs(b) > 1e-14)
            {
                double t = -c / b;
                if (t > 0 && t <= 1) roots[count++] = t;
            }
            return count;
        }
        double disc = b * b - 4 * a * c;
        if (disc < 0) return 0;
        double sq = Math.Sqrt(disc);
        double t1 = (-b - sq) / (2 * a);
        double t2 = (-b + sq) / (2 * a);
        if (t1 > 0 && t1 <= 1) roots[count++] = t1;
        if (t2 > 0 && t2 <= 1 && Math.Abs(t2 - t1) > 1e-10) roots[count++] = t2;
        return count;
    }

    /// <summary>
    /// Winding number contribution of a quadratic Bézier segment.
    /// Solves Y(t) = py and accumulates sign for each crossing where X(t) > px.
    /// </summary>
    public static int QuadWinding(
        double px, double py,
        double p0x, double p0y, double p1x, double p1y, double p2x, double p2y)
    {
        // Y(t) = (p0y - 2p1y + p2y)t² + 2(p1y - p0y)t + p0y = py
        double a = p0y - 2 * p1y + p2y;
        double b = 2 * (p1y - p0y);
        double c = p0y - py;

        Span<double> roots = stackalloc double[2];
        int n = SolveQuadratic(a, b, c, roots);

        int w = 0;
        for (int i = 0; i < n; i++)
        {
            double t = roots[i];
            var (ex, _) = QuadEval(t, p0x, p0y, p1x, p1y, p2x, p2y);
            if (ex > px)
            {
                // Sign from Y derivative at t
                var (_, dy) = QuadDeriv(t, p0x, p0y, p1x, p1y, p2x, p2y);
                w += dy > 0 ? 1 : -1;
            }
        }
        return w;
    }

    // ── Cubic Bézier ──────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (double x, double y) CubicEval(
        double t,
        double p0x, double p0y, double p1x, double p1y,
        double p2x, double p2y, double p3x, double p3y)
    {
        double u = 1 - t;
        double uu = u * u, tt = t * t;
        return (
            uu * u * p0x + 3 * uu * t * p1x + 3 * u * tt * p2x + tt * t * p3x,
            uu * u * p0y + 3 * uu * t * p1y + 3 * u * tt * p2y + tt * t * p3y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (double dx, double dy, double ddx, double ddy) CubicDerivs(
        double t,
        double p0x, double p0y, double p1x, double p1y,
        double p2x, double p2y, double p3x, double p3y)
    {
        double u = 1 - t;
        double dx = 3 * u * u * (p1x - p0x) + 6 * u * t * (p2x - p1x) + 3 * t * t * (p3x - p2x);
        double dy = 3 * u * u * (p1y - p0y) + 6 * u * t * (p2y - p1y) + 3 * t * t * (p3y - p2y);
        double ddx = 6 * u * (p2x - 2 * p1x + p0x) + 6 * t * (p3x - 2 * p2x + p1x);
        double ddy = 6 * u * (p2y - 2 * p1y + p0y) + 6 * t * (p3y - 2 * p2y + p1y);
        return (dx, dy, ddx, ddy);
    }

    public static double CubicDistance(
        double px, double py,
        double p0x, double p0y, double p1x, double p1y,
        double p2x, double p2y, double p3x, double p3y)
    {
        double best = double.MaxValue;

        ReadOnlySpan<double> seeds = [0.0, 0.2, 0.4, 0.6, 0.8, 1.0];
        foreach (double seed in seeds)
        {
            double t = seed;
            for (int i = 0; i < 6; i++)
            {
                var (cx, cy) = CubicEval(t, p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y);
                var (dx, dy, ddx, ddy) = CubicDerivs(t, p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y);
                double vx = cx - px, vy = cy - py;
                double f = vx * dx + vy * dy;
                double df = dx * dx + dy * dy + vx * ddx + vy * ddy;
                if (Math.Abs(df) < 1e-14) break;
                t -= f / df;
                t = Math.Clamp(t, 0, 1);
            }
            var (ex, ey) = CubicEval(t, p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y);
            double d = Math.Sqrt((px - ex) * (px - ex) + (py - ey) * (py - ey));
            if (d < best) best = d;
        }
        return best;
    }

    /// <summary>
    /// Cubic winding: find Y(t) = py roots in (0,1] by subdivision into monotone
    /// Y-intervals, then Newton refinement. Handles all cases including near-inflections.
    /// </summary>
    public static int CubicWinding(
        double px, double py,
        double p0x, double p0y, double p1x, double p1y,
        double p2x, double p2y, double p3x, double p3y)
    {
        // Find monotone intervals in Y by solving dY/dt = 0.
        // dY/dt expanded as quadratic A t^2 + B t + C (factor of 3 omitted — doesn't affect roots):
        double A = -3 * p0y + 9 * p1y - 9 * p2y + 3 * p3y;
        double B = 6 * p0y - 12 * p1y + 6 * p2y;
        double C = -3 * p0y + 3 * p1y;

        // critical t values
        Span<double> critT = stackalloc double[4];
        int nc = 0;
        critT[nc++] = 0;
        critT[nc++] = 1;

        double disc = B * B - 4 * A * C;
        if (Math.Abs(A) > 1e-10 && disc >= 0)
        {
            double sq = Math.Sqrt(disc);
            double t1 = (-B - sq) / (2 * A);
            double t2 = (-B + sq) / (2 * A);
            if (t1 > 0 && t1 < 1) critT[nc++] = t1;
            if (t2 > 0 && t2 < 1 && Math.Abs(t2 - t1) > 1e-10) critT[nc++] = t2;
        }
        else if (Math.Abs(A) <= 1e-10 && Math.Abs(B) > 1e-10)
        {
            double t1 = -C / B;
            if (t1 > 0 && t1 < 1) critT[nc++] = t1;
        }

        // Sort the critical t values
        critT = critT[..nc];
        critT.Sort();

        int w = 0;
        for (int i = 0; i + 1 < nc; i++)
        {
            double ta = critT[i], tb = critT[i + 1];
            var (_, ya) = CubicEval(ta, p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y);
            var (_, yb) = CubicEval(tb, p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y);

            // Does this monotone segment cross py?
            bool aUp = ya <= py, bUp = yb > py;
            bool aDn = ya > py, bDn = yb <= py;
            if (!aUp && !aDn) continue; // bracket doesn't straddle (or touch)

            if ((aUp && bUp) || (aDn && bDn)) continue; // no crossing in this interval

            // Bisect + Newton to find the crossing t
            double tMid = (ta + tb) * 0.5;
            for (int k = 0; k < 16; k++)
            {
                var (_, ym) = CubicEval(tMid, p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y);
                // Newton step
                var (_, dym, _, _) = CubicDerivs(tMid, p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y);
                if (Math.Abs(dym) > 1e-14) tMid -= (ym - py) / dym;
                tMid = Math.Clamp(tMid, ta, tb);
                if (Math.Abs(ym - py) < 1e-10) break;
            }

            var (xm, _) = CubicEval(tMid, p0x, p0y, p1x, p1y, p2x, p2y, p3x, p3y);
            if (xm > px)
            {
                // sign from overall Y direction of this interval
                w += yb > ya ? 1 : -1;
            }
        }
        return w;
    }

    // ── Arc shared solver ─────────────────────────────────────────────────────

    public static void SolveArc(
        double startX, double startY, double endX, double endY,
        double rx, double ry, double rotDeg, bool largeArc, bool sweep,
        out double adjRx, out double adjRy,
        out double cx, out double cy,
        out double startAngle, out double deltaAngle,
        out double cosR, out double sinR)
    {
        double rot = rotDeg * Math.PI / 180.0;
        cosR = Math.Cos(rot);
        sinR = Math.Sin(rot);

        double dx = (startX - endX) / 2.0;
        double dy = (startY - endY) / 2.0;
        double x1p = cosR * dx + sinR * dy;
        double y1p = -sinR * dx + cosR * dy;

        adjRx = Math.Abs(rx);
        adjRy = Math.Abs(ry);

        double rx2 = adjRx * adjRx, ry2 = adjRy * adjRy;
        double lam = (x1p * x1p) / rx2 + (y1p * y1p) / ry2;
        if (lam > 1.0)
        {
            double s = Math.Sqrt(lam);
            adjRx *= s; adjRy *= s;
            rx2 = adjRx * adjRx; ry2 = adjRy * adjRy;
        }

        double sign = (largeArc == sweep) ? -1.0 : 1.0;
        double sq = Math.Max(0.0,
            (rx2 * ry2 - rx2 * y1p * y1p - ry2 * x1p * x1p) /
            (rx2 * y1p * y1p + ry2 * x1p * x1p));
        double coef = sign * Math.Sqrt(sq);

        double cxp = coef * (adjRx * y1p / adjRy);
        double cyp = coef * (-adjRy * x1p / adjRx);

        cx = cosR * cxp - sinR * cyp + (startX + endX) / 2.0;
        cy = sinR * cxp + cosR * cyp + (startY + endY) / 2.0;

        startAngle = Math.Atan2((y1p - cyp) / adjRy, (x1p - cxp) / adjRx);
        deltaAngle = Math.Atan2((-y1p - cyp) / adjRy, (-x1p - cxp) / adjRx) - startAngle;

        if (!sweep && deltaAngle > 0) deltaAngle -= Math.Tau;
        if (sweep && deltaAngle < 0) deltaAngle += Math.Tau;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double NormalizeAngleFast(double a)
    {
        // Avoid while-loops: use modulo, then fix sign
        a %= Math.Tau;
        if (a < 0) a += Math.Tau;
        return a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AngleInArc(double t, double delta)
        => delta >= 0 ? t >= 0 && t <= delta : t <= 0 && t >= delta;

    public static double ArcDistance(
        double px, double py,
        double startX, double startY, double endX, double endY,
        double rx, double ry, double rotDeg, bool largeArc, bool sweep)
    {
        SolveArc(startX, startY, endX, endY, rx, ry, rotDeg, largeArc, sweep,
            out double adjRx, out double adjRy,
            out double cx, out double cy,
            out double startAngle, out double deltaAngle,
            out double cosR, out double sinR);

        if (adjRx <= 1e-12 || adjRy <= 1e-12)
        {
            double dx = px - endX, dy = py - endY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Transform point into ellipse-local space
        double dpx = px - cx, dpy = py - cy;
        double ex = (dpx * cosR + dpy * sinR) / adjRx;
        double ey = (-dpx * sinR + dpy * cosR) / adjRy;

        double angle = Math.Atan2(ey, ex);
        double t = NormalizeAngleFast(angle - startAngle);

        if (!AngleInArc(t, deltaAngle))
        {
            // Closest is one of the two endpoints
            double dsx = px - startX, dsy = py - startY;
            double dex = px - endX, dey = py - endY;
            double ds = dsx * dsx + dsy * dsy;
            double de = dex * dex + dey * dey;
            return Math.Sqrt(Math.Min(ds, de));
        }

        double finalAngle = startAngle + t;
        double closestX = cx + Math.Cos(finalAngle) * adjRx * cosR - Math.Sin(finalAngle) * adjRy * sinR;
        double closestY = cy + Math.Cos(finalAngle) * adjRx * sinR + Math.Sin(finalAngle) * adjRy * cosR;
        double dx2 = px - closestX, dy2 = py - closestY;
        return Math.Sqrt(dx2 * dx2 + dy2 * dy2);
    }

    /// <summary>
    /// Arc winding: finds Y crossings along the arc and accumulates sign.
    /// Samples the arc into short monotone pieces, then locates each crossing precisely.
    /// </summary>
    public static int ArcWinding(
        double px, double py,
        double startX, double startY, double endX, double endY,
        double rx, double ry, double rotDeg, bool largeArc, bool sweep)
    {
        SolveArc(startX, startY, endX, endY, rx, ry, rotDeg, largeArc, sweep,
            out double adjRx, out double adjRy,
            out double cx, out double cy,
            out double startAngle, out double deltaAngle,
            out double cosR, out double sinR);

        if (adjRx <= 1e-12 || adjRy <= 1e-12) return 0;

        // Parametric arc: P(u) where u in [0,1] maps to angle in [startAngle, startAngle+deltaAngle]
        // Subdivide into segments and find Y crossings
        int steps = Math.Max(8, (int)(Math.Abs(deltaAngle) / (Math.PI / 8)) + 1);
        double prevX = startX, prevY = startY;
        int w = 0;

        for (int i = 1; i <= steps; i++)
        {
            double u = i / (double)steps;
            double ang = startAngle + u * deltaAngle;
            double nx = cx + Math.Cos(ang) * adjRx * cosR - Math.Sin(ang) * adjRy * sinR;
            double ny = cy + Math.Cos(ang) * adjRx * sinR + Math.Sin(ang) * adjRy * cosR;

            w += LineWinding(px, py, prevX, prevY, nx, ny);

            prevX = nx; prevY = ny;
        }
        return w;
    }

    // ── Exact bounds helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Expands (minX,minY,maxX,maxY) to include point (x,y).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Expand(ref double minX, ref double minY, ref double maxX, ref double maxY,
        double x, double y)
    {
        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x > maxX) maxX = x;
        if (y > maxY) maxY = y;
    }

    /// <summary>
    /// Exact quadratic extrema: solves dX/dt=0 and dY/dt=0 and evaluates.
    /// </summary>
    public static Rect QuadAABB(double p0x, double p0y, double p1x, double p1y, double p2x, double p2y)
    {
        double minX = Math.Min(p0x, p2x), maxX = Math.Max(p0x, p2x);
        double minY = Math.Min(p0y, p2y), maxY = Math.Max(p0y, p2y);

        // dX/dt = 2(p1x-p0x)(1-t) + 2(p2x-p1x)t = 0 → t = (p0x-p1x)/(p0x-2p1x+p2x)
        static void ExtQ(double a0, double a1, double a2, ref double lo, ref double hi,
            double b0, double b1, double b2)
        {
            double denom = a0 - 2 * a1 + a2;
            if (Math.Abs(denom) < 1e-12) return;
            double t = (a0 - a1) / denom;
            if (t <= 0 || t >= 1) return;
            double u = 1 - t;
            double v = u * u * b0 + 2 * u * t * b1 + t * t * b2;
            if (v < lo) lo = v;
            if (v > hi) hi = v;
        }

        ExtQ(p0x, p1x, p2x, ref minX, ref maxX, p0x, p1x, p2x);
        ExtQ(p0y, p1y, p2y, ref minY, ref maxY, p0y, p1y, p2y);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Exact cubic extrema: solves quadratic dX/dt=0 and dY/dt=0.
    /// </summary>
    public static Rect CubicAABB(
        double p0x, double p0y, double p1x, double p1y,
        double p2x, double p2y, double p3x, double p3y)
    {
        double minX = Math.Min(p0x, p3x), maxX = Math.Max(p0x, p3x);
        double minY = Math.Min(p0y, p3y), maxY = Math.Max(p0y, p3y);

        static void ExtC(double a0, double a1, double a2, double a3, ref double lo, ref double hi,
            double b0, double b1, double b2, double b3)
        {
            // Derivative coefficients (quadratic): A t^2 + B t + C
            double A = -a0 + 3 * a1 - 3 * a2 + a3;
            double B = 2 * a0 - 4 * a1 + 2 * a2;
            double C = -a0 + a1;

            static double EvalCubic(double t, double q0, double q1, double q2, double q3)
            {
                double u = 1 - t;
                return u * u * u * q0 + 3 * u * u * t * q1 + 3 * u * t * t * q2 + t * t * t * q3;
            }

            static void TryT(double t, double b0, double b1, double b2, double b3, ref double lo, ref double hi)
            {
                if (t <= 0 || t >= 1) return;
                double v = EvalCubic(t, b0, b1, b2, b3);
                if (v < lo) lo = v;
                if (v > hi) hi = v;
            }

            if (Math.Abs(A) < 1e-12)
            {
                if (Math.Abs(B) > 1e-12) TryT(-C / B, b0, b1, b2, b3, ref lo, ref hi);
                return;
            }
            double disc = B * B - 4 * A * C;
            if (disc < 0) return;
            double sq = Math.Sqrt(disc);
            TryT((-B - sq) / (2 * A), b0, b1, b2, b3, ref lo, ref hi);
            TryT((-B + sq) / (2 * A), b0, b1, b2, b3, ref lo, ref hi);
        }

        ExtC(p0x, p1x, p2x, p3x, ref minX, ref maxX, p0x, p1x, p2x, p3x);
        ExtC(p0y, p1y, p2y, p3y, ref minY, ref maxY, p0y, p1y, p2y, p3y);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public static Rect LineAABB(double ax, double ay, double bx, double by)
    {
        double minX = Math.Min(ax, bx), maxX = Math.Max(ax, bx);
        double minY = Math.Min(ay, by), maxY = Math.Max(ay, by);
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public static Rect ArcAABB(
        double startX, double startY, double endX, double endY,
        double rx, double ry, double rotDeg, bool largeArc, bool sweep)
    {
        SolveArc(startX, startY, endX, endY, rx, ry, rotDeg, largeArc, sweep,
            out double adjRx, out double adjRy,
            out double cx, out double cy,
            out double startAngle, out double deltaAngle,
            out double cosR, out double sinR);

        double minX = Math.Min(startX, endX), maxX = Math.Max(startX, endX);
        double minY = Math.Min(startY, endY), maxY = Math.Max(startY, endY);

        // Check the four cardinal-angle extrema of the ellipse
        for (int j = 0; j < 4; j++)
        {
            double ang = j * (Math.PI / 2.0);
            double normT = NormalizeAngleFast(ang - startAngle);
            if (!AngleInArc(normT, deltaAngle)) continue;

            double ptX = cx + Math.Cos(ang) * adjRx * cosR - Math.Sin(ang) * adjRy * sinR;
            double ptY = cy + Math.Cos(ang) * adjRx * sinR + Math.Sin(ang) * adjRy * cosR;
            Expand(ref minX, ref minY, ref maxX, ref maxY, ptX, ptY);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}
