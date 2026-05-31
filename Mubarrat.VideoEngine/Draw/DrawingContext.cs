using Mubarrat.VideoEngine.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Draw;

public unsafe sealed class DrawingContext(Color32* firstPixel, ushort width, ushort height) : IRenderer
{
    private const double StrokeSampleWeight = 0.25;
    private const double MiterLimit = 4.0;
    private const double FillHorizontalEdgeEpsilon = 0.05;
    private const double FillSubpixelGrid = 4096.0;

    private readonly Stack<(Matrix2D Transform, double Opacity)> stateStack = new();
    private readonly Stack<InheritedPaintState> paintStack = new();

    private (Matrix2D Transform, double Opacity) CurrentState =>
        stateStack.Count > 0 ? stateStack.Peek() : (Matrix2D.Identity, 1);
    private InheritedPaintState CurrentPaint =>
        paintStack.Count > 0 ? paintStack.Peek() : new(null, default, Rect.NaN);

    public void PushTransform(Matrix2D transform) => PushState(transform, 1);
    public void PushOpacity(double opacity) => PushState(Matrix2D.Identity, opacity);

    public void PushState(Matrix2D transform, double opacity)
    {
        opacity = double.Clamp(opacity, 0, 1);
        var (ct, co) = CurrentState;
        stateStack.Push((transform * ct, co * opacity));
    }

    public void Pop() => stateStack.TryPop(out _);

    public void Draw(Drawing drawing)
    {
        if (CurrentState.Opacity == 0 || drawing.Opacity == 0) return;
        switch (drawing)
        {
            case PathDrawing pd:
                var ip = CurrentPaint;
                IBrush? ef = pd.Fill ?? ip.Fill;
                Pen es = pd.Stroke.Brush is null ? ip.Stroke : pd.Stroke;
                Rect? fb = pd.Fill is null ? NormalizeRectOrNull(ip.ScopeBounds) : null;
                Rect? sb = pd.Stroke.Brush is null ? NormalizeRectOrNull(ip.ScopeBounds) : null;
                PushState(pd.Transform, pd.Opacity);
                try { DrawPath(pd.Path * CurrentState.Transform, ef, es, fb, sb); }
                finally { Pop(); }
                break;

            case GroupDrawing gd:
                var pp = CurrentPaint;
                IBrush? gf = gd.Fill ?? pp.Fill;
                Pen gs = gd.Stroke.Brush is null ? pp.Stroke : gd.Stroke;
                Rect sc = (gd.Bounds * CurrentState.Transform).Normalized;
                if (!IsFiniteRect(sc)) sc = pp.ScopeBounds;
                paintStack.Push(new InheritedPaintState(gf, gs, sc));
                PushState(gd.Transform, gd.Opacity);
                try { gd.Drawings.ForEach(Draw); }
                finally { Pop(); paintStack.Pop(); }
                break;

            default: throw new NotImplementedException();
        }
    }

    void IRenderer.Draw(Drawing drawing) => Draw(drawing);
    public void Dispose() { }

    public void DrawPath(Path2D path, IBrush? fill, Pen stroke,
        Rect? fillSamplingBounds = null, Rect? strokeSamplingBounds = null)
    {
        if (path.Subpaths is null || path.Subpaths.Length == 0) return;
        var (transform, opacityD) = CurrentState;
        int w = width, h = height;
        if (w == 0 || h == 0) return;

        double invW = 1.0 / w, invH = 1.0 / h;
        float opacity = (float)opacityD;

        var db = (path.Bounds * transform).Normalized;
        var efb = fillSamplingBounds ?? db;
        var esb = strokeSamplingBounds ?? db;

        if (fill is not null)
            AAAFillRasterizer(path, fill, invW, invH, opacity, w, h, efb);
        if (stroke.Thickness > 0 && stroke.Brush is not null)
            StrokeRasterizer(path, stroke, transform, invW, invH, opacity, opacity >= 0.999999f, w, h, esb);
    }

    // =========================================================================
    //  AAA FILL — faithful port of Skia's blit_trapezoid_row algorithm
    //
    //  Two bugs fixed vs previous attempt:
    //
    //  BUG 1 — "straight edge flicker on morph frames"
    //    Cause: when two active edges cross inside a strip (can happen during
    //    morphing because the shape topology changes mid-frame), ll > lr after
    //    ClampEdgeToStrip.  We were just swapping them, but that silently
    //    produces wrong coverage for the adjacent strip.  Skia detects the
    //    cross and collapses both sides to the intersection point
    //    (approximate_intersection), then continues — no swap.
    //
    //  BUG 2 — "bracket → ± sign: fill/non-fill flip mid-morph"
    //    Cause: our right-boundary search scanned forward naively and sometimes
    //    picked the wrong right edge when multiple edges share the same rounded X
    //    at yMid (very common in morphing glyphs — many short edges cluster near
    //    integer Y values).  Skia pairs edges strictly: left edge i, right edge
    //    i+1 in sorted order, and processes them two at a time.  We now do the
    //    same: walk pairs under the winding rule, emitting one trapezoid per
    //    inside→outside transition rather than searching forward for the right
    //    boundary.
    // =========================================================================
    private void AAAFillRasterizer(
        Path2D path, IBrush fill,
        double invW, double invH, float opacity,
        int w, int h, Rect samplingBounds)
    {
        int maxEdges = 0;
        foreach (var sub in path.Subpaths)
            maxEdges += sub.Edges?.Length ?? 0;
        if (maxEdges == 0) return;

        AAAEdge* edges = (AAAEdge*)NativeMemory.AllocZeroed((nuint)maxEdges, (nuint)sizeof(AAAEdge));
        int maxY = maxEdges * 2 + h + 2;
        double* yVals = (double*)NativeMemory.Alloc((nuint)maxY, (nuint)sizeof(double));
        int* active = (int*)NativeMemory.Alloc((nuint)maxEdges, (nuint)sizeof(int));
        int* sorted = (int*)NativeMemory.Alloc((nuint)maxEdges, (nuint)sizeof(int));
        // +2: Skia notes one extra byte may be written at either end due to precision
        float* alphaRow = (float*)NativeMemory.AllocZeroed((nuint)(w + 2), (nuint)sizeof(float));

        try
        {
            int edgeCount = 0, yValCount = 0;
            foreach (var sub in path.Subpaths)
            {
                var es = sub.Edges;
                if (es is null) continue;
                foreach (var e in es)
                {
                    var (x1d, y1d) = e.Point1;
                    var (x2d, y2d) = e.Point2;
                    if (!double.IsFinite(x1d) || !double.IsFinite(y1d) ||
                        !double.IsFinite(x2d) || !double.IsFinite(y2d)) continue;

                    double dy = y2d - y1d;
                    if (Math.Abs(dy) < FillHorizontalEdgeEpsilon) continue;

                    double x1, y1, x2, y2; int winding;
                    if (y1d <= y2d) { x1 = x1d; y1 = y1d; x2 = x2d; y2 = y2d; winding = 1; }
                    else { x1 = x2d; y1 = y2d; x2 = x1d; y2 = y1d; winding = -1; }

                    if (y1 >= h || y2 <= 0) continue;
                    double cy1 = Math.Max(y1, 0.0);
                    double cy2 = Math.Min(y2, (double)h);
                    if (cy1 >= cy2) continue;

                    double dxDy = (x2 - x1) / (y2 - y1);
                    double upperX = SnapX(x1 + (cy1 - y1) * dxDy);

                    edges[edgeCount].UpperY = cy1;
                    edges[edgeCount].LowerY = cy2;
                    edges[edgeCount].X = upperX;
                    edges[edgeCount].DxDy = dxDy;
                    edges[edgeCount].Winding = winding;

                    if (yValCount + 2 < maxY)
                    {
                        yVals[yValCount++] = cy1;
                        yVals[yValCount++] = cy2;
                    }
                    edgeCount++;
                }
            }
            if (edgeCount == 0) return;

            for (int y = 0; y <= h && yValCount < maxY; y++)
                yVals[yValCount++] = (double)y;

            SortDoubles(yVals, yValCount);
            int uniqueY = 0;
            for (int i = 0; i < yValCount; i++)
            {
                double v = yVals[i];
                if (v < 0.0 || v > h) continue;
                if (uniqueY == 0 || yVals[uniqueY - 1] < v - 1e-10)
                    yVals[uniqueY++] = v;
            }

            double shapeLeft = samplingBounds.Left, shapeTop = samplingBounds.Top;
            double shapeW = samplingBounds.Width, shapeH = samplingBounds.Height;
            double invSW = shapeW != 0 ? 1.0 / shapeW : invW;
            double invSH = shapeH != 0 ? 1.0 / shapeH : invH;
            bool solidFill = fill is SolidColorBrush;
            Color32 solidPre = solidFill ? ((SolidColorBrush)fill).Color.ToPremultiplied : default;

            int currentPixelRow = -1;

            for (int si = 0; si + 1 < uniqueY; si++)
            {
                double y0 = yVals[si];
                double y1 = yVals[si + 1];
                double dy = y1 - y0;
                if (dy < 1e-10) continue;

                int pixelRow = (int)Math.Floor(y0);
                if ((uint)pixelRow >= (uint)h) continue;

                // Flush accumulated coverage when we move to the next integer row
                if (pixelRow != currentPixelRow)
                {
                    if (currentPixelRow >= 0)
                        ResolveRow(alphaRow, w, currentPixelRow,
                            shapeLeft, shapeTop, invSW, invSH,
                            opacity, solidFill, solidPre, fill);
                    currentPixelRow = pixelRow;
                }

                // Gather edges active in this strip
                int activeCount = 0;
                for (int i = 0; i < edgeCount; i++)
                    if (edges[i].UpperY < y1 - 1e-10 && edges[i].LowerY > y0 + 1e-10)
                        active[activeCount++] = i;
                if (activeCount == 0) continue;

                // Sort by X at strip midpoint, winding as tiebreaker (Skia: compare_edges)
                double yMid = (y0 + y1) * 0.5;
                for (int i = 0; i < activeCount; i++) sorted[i] = active[i];
                SortActiveAAAEdges(edges, sorted, activeCount, yMid);

                // ── Walk pairs under winding rule ─────────────────────────────
                // Skia's aaa_walk_edges processes edges strictly in sorted order,
                // pairing left and right boundaries.  Inside spans are toggled by
                // the winding state exactly as in classic scanline, but the
                // coverage is emitted analytically via blit_trapezoid_row.
                //
                // FIX for BUG 2: instead of searching forward for the "right
                // boundary group", we walk edge by edge, toggle winding, and
                // whenever we go from inside→outside we emit one trapezoid whose
                // left edge is the last "entered" edge and right edge is the
                // current "exited" edge.  This matches Skia's paired-edge model.

                int fillState = 0;
                int leftEdge = -1;
                double leftY0x = 0, leftY1x = 0; // X coords of left edge at y0,y1

                for (int i = 0; i < activeCount; i++)
                {
                    int ei = sorted[i];
                    bool wasInside = IsInside(fillState, path.IsNonZeroFill);
                    fillState += edges[ei].Winding;
                    bool nowInside = IsInside(fillState, path.IsNonZeroFill);

                    if (!wasInside && nowInside)
                    {
                        // Entering fill — record left edge
                        leftEdge = ei;
                        leftY0x = XAtY(ref edges[ei], y0);
                        leftY1x = XAtY(ref edges[ei], y1);
                        // Clamp to actual edge extent within this strip
                        EdgeExtentX(ref edges[ei], y0, y1, ref leftY0x, ref leftY1x);
                    }
                    else if (wasInside && !nowInside && leftEdge >= 0)
                    {
                        // Exiting fill — emit trapezoid [left, current]
                        double ul = leftY0x;
                        double ll = leftY1x;
                        double ur = XAtY(ref edges[ei], y0);
                        double lr = XAtY(ref edges[ei], y1);
                        EdgeExtentX(ref edges[ei], y0, y1, ref ur, ref lr);

                        // FIX for BUG 1: if edges crossed inside the strip,
                        // collapse to intersection (Skia: approximate_intersection)
                        // instead of swapping — swapping produces wrong alpha.
                        if (ll > lr)
                        {
                            double ix = ApproximateIntersection(ul, ll, ur, lr);
                            ul = ur = ix;
                            ll = lr = ix;
                        }
                        else
                        {
                            if (ul > ur) (ul, ur) = (ur, ul);
                        }

                        if (ul <= ur || ll <= lr)
                            BlitTrapezoidRow(alphaRow, w, ul, ur, ll, lr, dy);

                        leftEdge = -1;
                    }
                }
            }

            // Flush last row
            if (currentPixelRow >= 0)
                ResolveRow(alphaRow, w, currentPixelRow,
                    shapeLeft, shapeTop, invSW, invSH,
                    opacity, solidFill, solidPre, fill);
        }
        finally
        {
            NativeMemory.Free(edges);
            NativeMemory.Free(yVals);
            NativeMemory.Free(active);
            NativeMemory.Free(sorted);
            NativeMemory.Free(alphaRow);
        }
    }

    // ── Skia's blit_trapezoid_row, translated to C# doubles ──────────────────
    //
    // Starts with fullAlpha = dy for every pixel in [L, R).
    // Subtracts excluded area on the left  (below the left  edge line).
    // Subtracts excluded area on the right (above the right edge line).
    //
    // "below" and "above" here are Skia's compute_alpha_below_line /
    // compute_alpha_above_line, which compute the triangle/trapezoid area
    // excluded between the edge and the pixel column boundary.
    //
    // The key difference from our previous AccumulateTrapezoid:
    //   - We start from fullAlpha and SUBTRACT exclusions, exactly as Skia does.
    //   - This correctly handles the case where ul > ll or ur > lr (slanted left/
    //     right edges) which our previous (topOverlap+botOverlap)/2 formula
    //     silently got wrong when the edge slant exceeded one pixel width.
    //
    private static void BlitTrapezoidRow(
        float* alphaRow, int w,
        double ul, double ur, double ll, double lr,
        double dy)
    {
        // ul/ll are the left edge x at top/bottom; ur/lr are the right edge
        // Skia swaps so that ul<=ll and ur<=lr (the edge always goes "downward")
        if (ul > ll) (ul, ll) = (ll, ul);
        if (ur > lr) (ur, lr) = (lr, ur);

        // joinLeft: first pixel column entirely to the right of the left edge
        // joinRite: last  pixel column entirely to the left  of the right edge
        double joinLeft = Math.Ceiling(ll);  // = SkFixedCeilToFixed(ll) in Skia
        double joinRite = Math.Floor(ur);    // = SkFixedFloorToFixed(ur)

        if (joinLeft <= joinRite)
        {
            // There is a fully-covered interior rectangle [joinLeft, joinRite).
            // Blit left partial, full middle, right partial separately.

            if (ul < joinLeft)
                BlitLeftEdge(alphaRow, w, ul, joinLeft, ll, joinLeft, dy);

            // Full coverage rect: [joinLeft, joinRite), alpha = dy per pixel
            int fx = (int)joinLeft, tx = (int)joinRite;
            for (int x = fx; x < tx && x < w; x++)
                if (x >= 0) alphaRow[x] += (float)dy;

            if (lr > joinRite)
                BlitRightEdge(alphaRow, w, joinRite, ur, joinRite, lr, dy);
        }
        else
        {
            // Left and right edges overlap in X — single narrow trapezoid
            BlitAAARow(alphaRow, w, ul, ur, ll, lr, dy);
        }
    }

    // Emit coverage for the left partial strip [ul..joinLeft] × [0..dy]
    // where the left edge goes from ul (top) to ll (bottom).
    // Skia: the excluded area to the left of the edge is "below the line ul→ll".
    private static void BlitLeftEdge(
        float* alphaRow, int w,
        double ul, double ur, double ll, double lr, double dy)
    {
        // ul..ur is the top span, ll..lr is the bottom span
        // within [floor(ul), ceil(ll))
        BlitAAARow(alphaRow, w, ul, ur, ll, lr, dy);
    }

    private static void BlitRightEdge(
        float* alphaRow, int w,
        double ul, double ur, double ll, double lr, double dy)
    {
        BlitAAARow(alphaRow, w, ul, ur, ll, lr, dy);
    }

    // Core analytical trapezoid blit for a strip narrower than a few pixels.
    // Translates Skia's blit_aaa_trapezoid_row for the case where the
    // trapezoid spans only a few pixel columns.
    //
    // Method: start every pixel in [L,R) at fullAlpha=dy.
    // Subtract left exclusion (compute_alpha_below_line equivalent).
    // Subtract right exclusion (compute_alpha_above_line equivalent).
    //
    private static void BlitAAARow(
        float* alphaRow, int w,
        double ul, double ur, double ll, double lr,
        double dy)
    {
        int L = Math.Max(0, (int)Math.Floor(Math.Min(ul, ll)));
        int R = Math.Min(w, (int)Math.Ceiling(Math.Max(ur, lr)));
        if (L >= R) return;

        // Per-pixel initial alpha = dy (= fullAlpha in Skia, the strip height)
        // We compute each pixel's final coverage analytically.
        for (int x = L; x < R; x++)
        {
            double px = x, px1 = x + 1.0;

            // ── Left edge exclusion (area below the left edge, i.e. outside) ──
            // The left edge goes from ul (top) to ll (bottom).
            // Inside this pixel column the edge spans [max(ul,px), min(ll,px1)].
            double lTop = Math.Clamp(ul, px, px1);
            double lBot = Math.Clamp(ll, px, px1);
            // Width at top and bottom of the excluded left triangle/trapezoid
            double lWTop = lTop - px;   // how far from left of pixel to the edge top
            double lWBot = lBot - px;   // how far from left of pixel to the edge bot
            // The excluded area on the LEFT of the edge within this pixel:
            // it's a trapezoid with widths lWTop, lWBot and height dy.
            // Coverage excluded = (lWTop + lWBot) / 2 * dy
            double leftExcl = (lWTop + lWBot) * 0.5 * dy;

            // ── Right edge exclusion (area above the right edge, i.e. outside) ──
            double rTop = Math.Clamp(ur, px, px1);
            double rBot = Math.Clamp(lr, px, px1);
            double rWTop = px1 - rTop;   // how far from the edge top to right of pixel
            double rWBot = px1 - rBot;   // how far from the edge bot to right of pixel
            double rightExcl = (rWTop + rWBot) * 0.5 * dy;

            // Coverage = full strip height − excluded left − excluded right
            double coverage = dy - leftExcl - rightExcl;
            if (coverage < 0) coverage = 0;
            if (coverage > dy) coverage = dy;
            if (coverage < 1e-9) continue;

            alphaRow[x] += (float)coverage;
        }
    }

    // Skia: approximate_intersection — when ll > lr (left edge crossed right edge)
    // collapse both to the midpoint of the overlap
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double ApproximateIntersection(double ul, double ll, double ur, double lr)
    {
        // Normalise so each pair is [min,max]
        if (ul > ll) (ul, ll) = (ll, ul);
        if (ur > lr) (ur, lr) = (lr, ur);
        return (Math.Max(ul, ur) + Math.Min(ll, lr)) * 0.5;
    }

    // ── Resolve accumulated alpha row → premultiplied pixel blend ────────────
    private void ResolveRow(
        float* alphaRow, int w, int pixelRow,
        double shapeLeft, double shapeTop, double invSW, double invSH,
        float opacity, bool solidFill, Color32 solidPre, IBrush fill)
    {
        Color32* row = firstPixel + pixelRow * w;
        double sy = Math.Clamp((pixelRow + 0.5 - shapeTop) * invSH, 0.0, 1.0);

        for (int x = 0; x < w; x++)
        {
            float cov = alphaRow[x];
            alphaRow[x] = 0f;
            if (cov < 1e-5f) continue;
            if (cov > 1f) cov = 1f;

            float a = cov * opacity;
            if (a < 1e-5f) continue;

            Vector4 color;
            if (solidFill)
            {
                color = (Vector4)solidPre * a;
            }
            else
            {
                double sx = Math.Clamp((x + 0.5 - shapeLeft) * invSW, 0.0, 1.0);
                color = (Vector4)fill.Sample(sx, sy).ToPremultiplied * a;
            }
            Color32.BlendPremultiplied(color, ref row[x]);
        }
    }

    // ── Edge helpers ──────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SnapX(double x)
        => Math.Round(x * FillSubpixelGrid) / FillSubpixelGrid;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double XAtY(ref AAAEdge e, double y)
        => SnapX(e.X + e.DxDy * (y - e.UpperY));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsInside(int fillState, bool nonZero)
        => nonZero ? fillState != 0 : (fillState & 1) != 0;

    // Adjust edge X coordinates for the portion of this strip where the edge
    // actually exists (it may start or end mid-strip at a critical Y value).
    // Outside its Y extent the edge has zero contribution — we interpolate
    // toward the existing boundary, which is what Skia does implicitly because
    // edge endpoints are always critical Y values, so a strip never extends
    // beyond an edge endpoint.  Here we enforce it explicitly because our edge
    // table is built once and re-queried per strip.
    private static void EdgeExtentX(
        ref AAAEdge e, double y0, double y1,
        ref double xTop, ref double xBot)
    {
        double stripH = y1 - y0;
        if (stripH < 1e-10) return;

        if (e.UpperY > y0 + 1e-10)
        {
            // Edge starts partway through the strip.
            // Above e.UpperY: width on this side is zero.
            // xTop should be e.X (the actual start X).
            // Linearly interpolate: at the fraction of the strip below e.UpperY
            // we switch from "edge not present" to "edge present".
            // The simplest correct model: the "virtual" top is at the same X
            // as the actual start (no area above e.UpperY).
            xTop = e.X;
        }

        if (e.LowerY < y1 - 1e-10)
        {
            // Edge ends partway through the strip.
            xBot = XAtY(ref e, e.LowerY);
        }
    }

    private static void SortActiveAAAEdges(AAAEdge* edges, int* sorted, int count, double yMid)
    {
        // Insertion sort — Skia's compare_edges: sort by X at yMid, then by DxDy
        for (int i = 1; i < count; i++)
        {
            int key = sorted[i];
            double keyX = XAtY(ref edges[key], yMid);
            double keyD = edges[key].DxDy;
            int keyW = edges[key].Winding;
            int j = i - 1;
            while (j >= 0)
            {
                double jX = XAtY(ref edges[sorted[j]], yMid);
                double jD = edges[sorted[j]].DxDy;
                // Primary: X; Secondary: DxDy (slope); Tertiary: winding
                bool swap = jX > keyX + 1e-10
                    || (Math.Abs(jX - keyX) <= 1e-10 && jD > keyD + 1e-10)
                    || (Math.Abs(jX - keyX) <= 1e-10 && Math.Abs(jD - keyD) <= 1e-10 && edges[sorted[j]].Winding > keyW);
                if (!swap) break;
                sorted[j + 1] = sorted[j];
                j--;
            }
            sorted[j + 1] = key;
        }
    }

    private static void SortDoubles(double* arr, int count)
    {
        for (int i = 1; i < count; i++)
        {
            double key = arr[i]; int j = i - 1;
            while (j >= 0 && arr[j] > key) { arr[j + 1] = arr[j]; j--; }
            arr[j + 1] = key;
        }
    }

    private struct AAAEdge
    {
        public double UpperY, LowerY;
        public double X;        // X at UpperY, snapped
        public double DxDy;     // horizontal change per unit vertical
        public int Winding;
    }

    // =========================================================================
    //  STROKE RASTERIZER  (4×MSAA — correct for curved strokes)
    // =========================================================================

    private void StrokeRasterizer(Path2D path, Pen stroke, Matrix2D transform,
        double invW, double invH, float opacity, bool fullOpacity, int w, int h, Rect samplingBounds)
    {
        double radius = stroke.Thickness * 0.5;
        double r2 = radius * radius;

        foreach (var sub in path.Subpaths)
        {
            var es = sub.Edges;
            if (es is null || es.Length == 0) continue;

            double[]? dash = stroke.DashPattern;
            bool useDash = dash is { Length: > 0 };

            if (!useDash)
            {
                int segmentCount = es.Length;
                var segs = new StrokeSegmentInfo[segmentCount];
                for (int i = 0; i < segmentCount; i++)
                {
                    ref readonly var e = ref es[i];
                    var (x1, y1) = e.Point1; var (x2, y2) = e.Point2;
                    double dx = x2 - x1, dy = y2 - y1, len = Math.Sqrt(dx * dx + dy * dy);
                    if (len <= 0)
                    {
                        segs[i] = new(x1, y1, x2, y2, 0, 0, 0, 0, 0, false);
                        DrawStrokePoint(firstPixel, w, h, x1, y1, radius, r2, stroke, invW, invH, opacity, samplingBounds);
                        continue;
                    }
                    double ux = dx / len, uy = dy / len, vx = -uy, vy = ux;
                    segs[i] = new(x1, y1, x2, y2, ux, uy, vx, vy, len, true);
                    DrawStrokeSegment(firstPixel, w, h, x1, y1, x2, y2, ux, uy, vx, vy, len, radius, r2, stroke, stroke.Cap, invW, invH, opacity, samplingBounds);
                }
                bool closed = segmentCount > 1
                    && NearlyEqual(segs[segmentCount - 1].X2, segs[0].X1)
                    && NearlyEqual(segs[segmentCount - 1].Y2, segs[0].Y1);
                int joinCount = closed ? segmentCount : segmentCount - 1;
                for (int i = 0; i < joinCount; i++)
                {
                    int ni = (i + 1) % segmentCount;
                    ref readonly var cur = ref segs[i]; ref readonly var nxt = ref segs[ni];
                    if (!cur.IsValid || !nxt.IsValid) continue;
                    if (!NearlyEqual(cur.X2, nxt.X1) || !NearlyEqual(cur.Y2, nxt.Y1)) continue;
                    DrawStrokeJoin(firstPixel, w, h, nxt.X1, nxt.Y1, cur.Ux, cur.Uy, nxt.Ux, nxt.Uy, radius, r2, stroke, stroke.Join, invW, invH, opacity, samplingBounds);
                }
                continue;
            }

            int dashIndex = 0; double dashRemaining = dash![0] * stroke.Thickness; bool dashOn = true;
            for (int i = 0; i < es.Length; i++)
            {
                ref readonly var e = ref es[i];
                var (x1, y1) = e.Point1; var (x2, y2) = e.Point2;
                double dx = x2 - x1, dy = y2 - y1, len = Math.Sqrt(dx * dx + dy * dy);
                if (len <= 0) { DrawStrokePoint(firstPixel, w, h, x1, y1, radius, r2, stroke, invW, invH, opacity, samplingBounds); continue; }
                double ux = dx / len, uy = dy / len, vx = -uy, vy = ux, consumed = 0;
                while (consumed < len)
                {
                    if (dashRemaining <= 0) { dashIndex = (dashIndex + 1) % dash.Length; dashRemaining = dash[dashIndex] * stroke.Thickness; if (dashRemaining <= 0) continue; dashOn = !dashOn; }
                    double step = Math.Min(dashRemaining, len - consumed);
                    if (dashOn) { double t0 = consumed / len, t1 = (consumed + step) / len; DrawStrokeSegment(firstPixel, w, h, x1 + dx * t0, y1 + dy * t0, x1 + dx * t1, y1 + dy * t1, ux, uy, vx, vy, step, radius, r2, stroke, stroke.Cap, invW, invH, opacity, samplingBounds); }
                    consumed += step; dashRemaining -= step;
                }
            }
        }
    }

    private void DrawStrokeJoin(Color32* fp, int w, int h, double cx, double cy,
        double ux0, double uy0, double ux1, double uy1,
        double radius, double r2, Pen stroke, LineJoin join,
        double invW, double invH, double opacity, Rect sb)
    {
        double turn = ux0 * uy1 - uy0 * ux1; if (Math.Abs(turn) <= 1e-10) return;
        double n0x = -uy0, n0y = ux0, n1x = -uy1, n1y = ux1;
        if (turn < 0) { n0x = -n0x; n0y = -n0y; n1x = -n1x; n1y = -n1y; }
        double ax = cx + n0x * radius, ay = cy + n0y * radius, bx = cx + n1x * radius, by = cy + n1y * radius;
        switch (join)
        {
            case LineJoin.Round: DrawRoundJoin(fp, w, h, cx, cy, ax, ay, bx, by, r2, stroke, invW, invH, opacity, sb); return;
            case LineJoin.Miter:
                if (TryIntersectLines(ax, ay, ux0, uy0, bx, by, ux1, uy1, out double mx, out double my))
                { double ml = Math.Sqrt((mx - cx) * (mx - cx) + (my - cy) * (my - cy)); if (ml <= radius * MiterLimit) { DrawTriangleJoin(fp, w, h, ax, ay, mx, my, bx, by, stroke, invW, invH, opacity, sb); return; } }
                break;
        }
        DrawTriangleJoin(fp, w, h, cx, cy, ax, ay, bx, by, stroke, invW, invH, opacity, sb);
    }

    private void DrawRoundJoin(Color32* fp, int w, int h, double cx, double cy,
        double ax, double ay, double bx, double by, double r2,
        Pen stroke, double invW, double invH, double opacity, Rect sb)
    {
        double r = Math.Sqrt(r2);
        int x0 = Math.Clamp((int)Math.Floor(cx - r), 0, w - 1), x1 = Math.Clamp((int)Math.Ceiling(cx + r), 0, w - 1);
        int y0 = Math.Clamp((int)Math.Floor(cy - r), 0, h - 1), y1 = Math.Clamp((int)Math.Ceiling(cy + r), 0, h - 1);
        double sl = sb.Left, st = sb.Top, sw = sb.Width, sh = sb.Height;
        double isw = sw != 0 ? 1.0 / sw : invW, ish = sh != 0 ? 1.0 / sh : invH;
        double avx = ax - cx, avy = ay - cy, bvx = bx - cx, bvy = by - cy, cab = avx * bvy - avy * bvx;
        for (int py = y0; py <= y1; py++)
        {
            Color32* row = fp + py * w; double cys = py + 0.5;
            for (int px = x0; px <= x1; px++)
            {
                int c = 0;
                c += IsRoundJoinSampleHit(px + 0.25, py + 0.25, cx, cy, r2, avx, avy, bvx, bvy, cab) ? 1 : 0;
                c += IsRoundJoinSampleHit(px + 0.75, py + 0.25, cx, cy, r2, avx, avy, bvx, bvy, cab) ? 1 : 0;
                c += IsRoundJoinSampleHit(px + 0.25, py + 0.75, cx, cy, r2, avx, avy, bvx, bvy, cab) ? 1 : 0;
                c += IsRoundJoinSampleHit(px + 0.75, py + 0.75, cx, cy, r2, avx, avy, bvx, bvy, cab) ? 1 : 0;
                if (c <= 0) continue;
                double sx = Math.Clamp((px + 0.5 - sl) * isw, 0.0, 1.0), sy = Math.Clamp((cys - st) * ish, 0.0, 1.0);
                Color32.BlendPremultiplied(stroke.Sample(sx, sy).ToPremultiplied * (c * StrokeSampleWeight * opacity), ref row[px]);
            }
        }
    }

    private void DrawTriangleJoin(Color32* fp, int w, int h,
        double x1, double y1, double x2, double y2, double x3, double y3,
        Pen stroke, double invW, double invH, double opacity, Rect sb)
    {
        int minX = Math.Clamp((int)Math.Floor(Math.Min(x1, Math.Min(x2, x3))), 0, w - 1);
        int maxX = Math.Clamp((int)Math.Ceiling(Math.Max(x1, Math.Max(x2, x3))), 0, w - 1);
        int minY = Math.Clamp((int)Math.Floor(Math.Min(y1, Math.Min(y2, y3))), 0, h - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(Math.Max(y1, Math.Max(y2, y3))), 0, h - 1);
        double sl = sb.Left, st = sb.Top, sw = sb.Width, sh = sb.Height;
        double isw = sw != 0 ? 1.0 / sw : invW, ish = sh != 0 ? 1.0 / sh : invH;
        for (int py = minY; py <= maxY; py++)
        {
            Color32* row = fp + py * w; double cy = py + 0.5;
            for (int px = minX; px <= maxX; px++)
            {
                int c = 0;
                c += IsPointInTriangle(px + 0.25, py + 0.25, x1, y1, x2, y2, x3, y3) ? 1 : 0;
                c += IsPointInTriangle(px + 0.75, py + 0.25, x1, y1, x2, y2, x3, y3) ? 1 : 0;
                c += IsPointInTriangle(px + 0.25, py + 0.75, x1, y1, x2, y2, x3, y3) ? 1 : 0;
                c += IsPointInTriangle(px + 0.75, py + 0.75, x1, y1, x2, y2, x3, y3) ? 1 : 0;
                if (c <= 0) continue;
                double sx = Math.Clamp((px + 0.5 - sl) * isw, 0.0, 1.0), sy = Math.Clamp((cy - st) * ish, 0.0, 1.0);
                Color32.BlendPremultiplied(stroke.Sample(sx, sy).ToPremultiplied * (c * StrokeSampleWeight * opacity), ref row[px]);
            }
        }
    }

    private static bool IsRoundJoinSampleHit(double x, double y, double cx, double cy, double r2,
        double avx, double avy, double bvx, double bvy, double crossAB)
    {
        double vx = x - cx, vy = y - cy; if (vx * vx + vy * vy > r2) return false;
        double ca = avx * vy - avy * vx, cb = vx * bvy - vy * bvx;
        return crossAB >= 0 ? ca >= 0 && cb >= 0 : ca <= 0 && cb <= 0;
    }

    private static bool IsPointInTriangle(double px, double py,
        double x1, double y1, double x2, double y2, double x3, double y3)
    {
        double d1 = Sign(px, py, x1, y1, x2, y2), d2 = Sign(px, py, x2, y2, x3, y3), d3 = Sign(px, py, x3, y3, x1, y1);
        return !(d1 < 0 || d2 < 0 || d3 < 0) || !(d1 > 0 || d2 > 0 || d3 > 0);
    }

    private static double Sign(double px, double py, double x1, double y1, double x2, double y2)
        => (px - x2) * (y1 - y2) - (x1 - x2) * (py - y2);

    private static bool TryIntersectLines(double x1, double y1, double dx1, double dy1,
        double x2, double y2, double dx2, double dy2, out double ix, out double iy)
    {
        double det = dx1 * dy2 - dy1 * dx2; if (Math.Abs(det) <= 1e-12) { ix = 0; iy = 0; return false; }
        double t = ((x2 - x1) * dy2 - (y2 - y1) * dx2) / det;
        ix = x1 + dx1 * t; iy = y1 + dy1 * t; return double.IsFinite(ix) && double.IsFinite(iy);
    }

    private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) <= 1e-6;

    private void DrawStrokePoint(Color32* fp, int w, int h, double x, double y,
        double radius, double r2, Pen stroke, double invW, double invH, double opacity, Rect sb)
    {
        int x0 = Math.Clamp((int)(x - radius), 0, w - 1), x1 = Math.Clamp((int)(x + radius), 0, w - 1);
        int y0 = Math.Clamp((int)(y - radius), 0, h - 1), y1 = Math.Clamp((int)(y + radius), 0, h - 1);
        double sl = sb.Left, st = sb.Top, sw = sb.Width, sh = sb.Height;
        double isw = sw != 0 ? 1.0 / sw : invW, ish = sh != 0 ? 1.0 / sh : invH;
        for (int py = y0; py <= y1; py++)
        {
            Color32* row = fp + py * w; double cy = py + 0.5;
            for (int px = x0; px <= x1; px++)
            {
                int c = 0; double ax = px + 0.25, bx = px + 0.75, ay = py + 0.25, by = py + 0.75, dx, dy;
                dx = ax - x; dy = ay - y; if (dx * dx + dy * dy <= r2) c++;
                dx = bx - x; dy = ay - y; if (dx * dx + dy * dy <= r2) c++;
                dx = ax - x; dy = by - y; if (dx * dx + dy * dy <= r2) c++;
                dx = bx - x; dy = by - y; if (dx * dx + dy * dy <= r2) c++;
                if (c > 0)
                {
                    double sx = (px + 0.5 - sl) * isw, sy = (cy - st) * ish;
                    Color32.BlendPremultiplied(stroke.Sample(Math.Clamp(sx, 0.0, 1.0), Math.Clamp(sy, 0.0, 1.0)).ToPremultiplied * (opacity * c * StrokeSampleWeight), ref row[px]);
                }
            }
        }
    }

    private void DrawStrokeSegment(Color32* fp, int w, int h,
        double x1, double y1, double x2, double y2,
        double ux, double uy, double vx, double vy, double len,
        double radius, double r2, Pen stroke, LineCap cap,
        double invW, double invH, double opacity, Rect sb)
    {
        double pad = cap == LineCap.Flat ? 0 : radius;
        int minX = Math.Clamp((int)Math.Floor(Math.Min(x1, x2) - pad - radius), 0, w - 1);
        int maxX = Math.Clamp((int)Math.Ceiling(Math.Max(x1, x2) + pad + radius), 0, w - 1);
        int minY = Math.Clamp((int)Math.Floor(Math.Min(y1, y2) - pad - radius), 0, h - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(Math.Max(y1, y2) + pad + radius), 0, h - 1);
        double sl = sb.Left, st = sb.Top, sw = sb.Width, sh = sb.Height;
        double isw = sw != 0 ? 1.0 / sw : invW, ish = sh != 0 ? 1.0 / sh : invH;
        for (int py = minY; py <= maxY; py++)
        {
            Color32* row = fp + py * w; double cy = py + 0.5;
            for (int px = minX; px <= maxX; px++)
            {
                int c = 0;
                c += IsStrokeSampleHit(px + 0.25, py + 0.25, x1, y1, x2, y2, ux, uy, vx, vy, len, radius, r2, cap) ? 1 : 0;
                c += IsStrokeSampleHit(px + 0.75, py + 0.25, x1, y1, x2, y2, ux, uy, vx, vy, len, radius, r2, cap) ? 1 : 0;
                c += IsStrokeSampleHit(px + 0.25, py + 0.75, x1, y1, x2, y2, ux, uy, vx, vy, len, radius, r2, cap) ? 1 : 0;
                c += IsStrokeSampleHit(px + 0.75, py + 0.75, x1, y1, x2, y2, ux, uy, vx, vy, len, radius, r2, cap) ? 1 : 0;
                if (c > 0)
                {
                    double sx = (px + 0.5 - sl) * isw, sy = (cy - st) * ish;
                    Color32.BlendPremultiplied(stroke.Sample(Math.Clamp(sx, 0.0, 1.0), Math.Clamp(sy, 0.0, 1.0)).ToPremultiplied * (opacity * c * StrokeSampleWeight), ref row[px]);
                }
            }
        }
    }

    private static bool IsStrokeSampleHit(double sx, double sy,
        double x1, double y1, double x2, double y2,
        double ux, double uy, double vx, double vy,
        double len, double radius, double r2, LineCap cap)
    {
        double rx = sx - x1, ry = sy - y1, along = rx * ux + ry * uy, across = rx * vx + ry * vy;
        return cap switch
        {
            LineCap.Flat => along >= 0 && along <= len && Math.Abs(across) <= radius,
            LineCap.Square => along >= -radius && along <= len + radius && Math.Abs(across) <= radius,
            LineCap.Round => (along >= 0 && along <= len && Math.Abs(across) <= radius)
                           || (along < 0 && rx * rx + ry * ry <= r2)
                           || (along > len && (sx - x2) * (sx - x2) + (sy - y2) * (sy - y2) <= r2),
            _ => along >= 0 && along <= len && Math.Abs(across) <= radius
        };
    }

    private static Rect? NormalizeRectOrNull(Rect rect) => IsFiniteRect(rect) ? rect.Normalized : null;
    private static bool IsFiniteRect(Rect rect)
        => double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.Width) && double.IsFinite(rect.Height);

    private readonly struct StrokeSegmentInfo(
        double x1, double y1, double x2, double y2,
        double ux, double uy, double vx, double vy, double len, bool isValid)
    {
        public readonly double X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Ux = ux, Uy = uy, Vx = vx, Vy = vy, Len = len;
        public readonly bool IsValid = isValid;
    }

    private readonly record struct InheritedPaintState(IBrush? Fill, Pen Stroke, Rect ScopeBounds);
}
