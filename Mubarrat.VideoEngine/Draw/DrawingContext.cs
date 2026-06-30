using Mubarrat.VideoEngine.Path;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Draw;

// ─────────────────────────────────────────────────────────────────────────────
// DrawingContext
//
// Pixel-accurate software renderer for Path2D → Color32* pixel buffer.
//
// FILL: Blend2D / AGG / FreeType cell rasterizer.
//   • Walks Path2D → PathContour → IPathSegment (LineSegment / Quadratic / Cubic).
//   • Adaptive curve flattening (tolerance 0.25 px): quadratic & cubic.
//   • Fixed-point 24.8 coordinates (Sub = 256 subpixel levels per pixel).
//   • Dense int area[] and cover[] tables, one entry per active row/column.
//   • Per-row ulong bitvectors — mark touched columns; resolve skips empty words.
//   • AGG/Blend2D-style top-to-bottom resolve after all edges are accumulated.
//   • Exact per-row edge splitting — dy is distributed across crossed cells.
//   • Resolve: alpha = FillRule(running × 2×Sub − area[x]) / (2×Sub²).
//
// STROKE: Analytic outline fill from flattened segments.
//   • Same adaptive flattening pipeline.
//   • Caps, joins, and dashes are emitted as fillable contours.
//   • The fill rasterizer handles AA, so stroke stays branch-light.
// ─────────────────────────────────────────────────────────────────────────────
public unsafe sealed class DrawingContext(Color32* firstPixel, ushort width, ushort height) : IRenderer
{
    // ── Subpixel constants ────────────────────────────────────────────────────
    // Fixed-point 24.8: multiply world coords by Sub to get integer coords.
    private const int Sub = 256;        // subpixel levels per pixel
    private const int SubBits = 8;          // log2(Sub)
    private const int SubMask = Sub - 1;    // 0xFF
    // Resolve denominator: 2 × Sub² = 131072
    // area accumulates in sub-pixel² units; normalise by this at blend time.
    private const int AreaNorm = 2 * Sub * Sub;

    // Adaptive flattening tolerance (squared, for cheap distance test)
    private const double FlatTol2 = 0.25 * 0.25;

    private const double MiterLim = 10.0;

    // ── State stacks ──────────────────────────────────────────────────────────
    private readonly Stack<(Matrix2D Transform, double Opacity)> stateStack = new();
    private readonly Stack<InheritedPaintState> paintStack = new();

    private (Matrix2D Transform, double Opacity) CurrentState =>
        stateStack.Count > 0 ? stateStack.Peek() : (Matrix2D.Identity, 1);

    private InheritedPaintState CurrentPaint =>
        paintStack.Count > 0 ? paintStack.Peek() : new(null, default, Rect.NaN);

    public void PushTransform(Matrix2D transform) => PushState(transform, 1);
    public void PushOpacity(double opacity) => PushState(Matrix2D.Identity, opacity);

    internal void PushState(Matrix2D transform, double opacity)
    {
        opacity = double.Clamp(opacity, 0, 1);
        var (ct, co) = CurrentState;
        stateStack.Push((transform * ct, co * opacity));
    }

    public void Pop() => stateStack.TryPop(out _);

    // ── Draw dispatch ─────────────────────────────────────────────────────────
    public void Draw(Drawing drawing)
    {
        if (CurrentState.Opacity == 0 || drawing.Opacity == 0) return;
        switch (drawing)
        {
            case PathDrawing pd:
                {
                    var ip = CurrentPaint;
                    IBrush? ef = pd.Fill ?? ip.Fill;
                    Pen es = pd.Stroke.Brush is null ? ip.Stroke : pd.Stroke;
                    Rect? fb = pd.Fill is null ? NormalizeRectOrNull(ip.ScopeBounds) : null;
                    Rect? sb = pd.Stroke.Brush is null ? NormalizeRectOrNull(ip.ScopeBounds) : null;
                    PushState(pd.Transform, pd.Opacity);
                    try { DrawPath(pd.Path * CurrentState.Transform, ef, es, fb, sb); }
                    finally { Pop(); }
                    break;
                }
            case GroupDrawing gd:
                {
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
                }
            default: throw new NotImplementedException();
        }
    }

    void IRenderer.Draw(Drawing drawing) => Draw(drawing);
    public void Dispose() { }

    public void DrawPath(Path2D path, IBrush? fill, Pen stroke,
        Rect? fillSamplingBounds = null, Rect? strokeSamplingBounds = null)
    {
        if (path.Count == 0) return;
        var (_, opacityD) = CurrentState;
        int w = width, h = height;
        if (w == 0 || h == 0) return;

        float opacity = (float)opacityD;
        Rect db = path.Bounds;

        if (fill is not null)
            CellFill(path, fill, opacity, w, h, fillSamplingBounds ?? db);

        if (stroke.Thickness > 0 && stroke.Brush is not null)
            StrokePass(path, stroke, opacity, w, h, strokeSamplingBounds ?? db);
    }

    // =========================================================================
    //  CELL FILL RASTERIZER
    //
    //  Exact AGG / FreeType / Blend2D algorithm.
    //
    //  For each IPathSegment in each PathContour:
    //    LineSegment     → RenderLine directly.
    //    QuadraticSegment→ adaptive flatten → RenderLine per flat piece.
    //    CubicSegment    → adaptive flatten → RenderLine per flat piece.
    //
    //  RenderLine converts to fixed-point, clips to canvas, then calls
    //  RenderHLine for each pixel row the segment crosses.
    //
    //  RenderHLine is the AGG cell accumulator:
    //    Single column:  area += (lx1+lx2)*dy;  cover += dy
    //    Multi-column:   split at each crossed pixel boundary, distributing dy
    //                    proportionally so all cell deltas sum exactly to dy.
    //
    //  ResolveRow:  walk bitvector word-by-word, add cover before calculating
    //    each cell, then apply the non-zero/even-odd alpha rule to both edge
    //    pixels and spans between edge pixels.
    // =========================================================================
    private void CellFill(
        Path2D path, IBrush fill, float opacity,
        int w, int h, Rect samplingBounds)
    {
        Rect bounds = path.Bounds;
        int rowStart, rowEnd;
        if (IsFiniteRect(bounds))
        {
            bounds = bounds.Normalized;
            rowStart = Math.Clamp((int)Math.Floor(bounds.Top), 0, h);
            rowEnd = Math.Clamp((int)Math.Ceiling(bounds.Bottom), 0, h);
        }
        else
        {
            rowStart = 0;
            rowEnd = h;
        }
        if (rowStart >= rowEnd) return;

        int rowCount = rowEnd - rowStart;
        int bvWords = (w + 63) >> 6;
        nuint cellCount = (nuint)rowCount * (nuint)w;
        nuint bitCount = (nuint)rowCount * (nuint)bvWords;
        int* area = (int*)NativeMemory.AllocZeroed(cellCount, (nuint)sizeof(int));
        int* cover = (int*)NativeMemory.AllocZeroed(cellCount, (nuint)sizeof(int));
        ulong* bv = (ulong*)NativeMemory.AllocZeroed(bitCount, (nuint)sizeof(ulong));
        int* minWord = (int*)NativeMemory.Alloc((nuint)rowCount, (nuint)sizeof(int));
        int* maxWord = (int*)NativeMemory.Alloc((nuint)rowCount, (nuint)sizeof(int));
        for (int i = 0; i < rowCount; i++)
        {
            minWord[i] = bvWords;
            maxWord[i] = -1;
        }

        double sl = samplingBounds.Left, st = samplingBounds.Top;
        double sw = samplingBounds.Width, sh = samplingBounds.Height;
        double invW = sw > 1e-9 ? 1.0 / sw : 1.0 / w;
        double invH = sh > 1e-9 ? 1.0 / sh : 1.0 / h;
        bool solid = fill is SolidColorBrush;
        Color32 solPre = solid ? ((SolidColorBrush)fill).Color.ToPremultiplied : default;
        bool nonZero = path.FillRule == FillRule.NonZero;

        try
        {
            foreach (PathContour contour in path)
            {
                foreach (IPathSegment seg in contour)
                {
                    switch (seg)
                    {
                        case LineSegment l:
                            RenderLine(l.Start.X, l.Start.Y, l.End.X, l.End.Y);
                            break;
                        case QuadraticSegment q:
                            FlatQ(q.Start.X, q.Start.Y, q.Control.X, q.Control.Y, q.End.X, q.End.Y);
                            break;
                        case CubicSegment c:
                            FlatC(c.Start.X, c.Start.Y,
                                  c.Control1.X, c.Control1.Y,
                                  c.Control2.X, c.Control2.Y,
                                  c.End.X, c.End.Y);
                            break;
                    }
                }
            }
            for (int ry = 0; ry < rowCount; ry++)
            {
                if (maxWord[ry] < minWord[ry]) continue;

                ResolveRow(rowStart + ry, w,
                    area + ry * w, cover + ry * w, bv + ry * bvWords,
                    minWord[ry], maxWord[ry],
                    sl, st, invW, invH, opacity, nonZero, solid, solPre, fill);
            }
        }
        finally
        {
            NativeMemory.Free(area);
            NativeMemory.Free(cover);
            NativeMemory.Free(bv);
            NativeMemory.Free(minWord);
            NativeMemory.Free(maxWord);
        }

        // ── Adaptive flattening ───────────────────────────────────────────────

        void FlatQ(double x0, double y0, double x1, double y1, double x2, double y2)
        {
            // Flatness: distance² from control to chord midpoint
            double mx = (x0 + x2) * 0.5, my = (y0 + y2) * 0.5;
            double ex = x1 - mx, ey = y1 - my;
            if (ex * ex + ey * ey <= FlatTol2) { RenderLine(x0, y0, x2, y2); return; }
            // de Casteljau at t=0.5
            double m01x = (x0 + x1) * 0.5, m01y = (y0 + y1) * 0.5;
            double m12x = (x1 + x2) * 0.5, m12y = (y1 + y2) * 0.5;
            double mmx = (m01x + m12x) * 0.5, mmy = (m01y + m12y) * 0.5;
            FlatQ(x0, y0, m01x, m01y, mmx, mmy);
            FlatQ(mmx, mmy, m12x, m12y, x2, y2);
        }

        void FlatC(double x0, double y0, double x1, double y1,
                   double x2, double y2, double x3, double y3)
        {
            // Flatness: max squared distance of control points from chord
            double cdx = x3 - x0, cdy = y3 - y0, cl2 = cdx * cdx + cdy * cdy;
            double d1, d2;
            if (cl2 < 1e-10)
            {
                double e1x = x1 - x0, e1y = y1 - y0; d1 = e1x * e1x + e1y * e1y;
                double e2x = x2 - x0, e2y = y2 - y0; d2 = e2x * e2x + e2y * e2y;
            }
            else
            {
                double il = 1.0 / cl2;
                double t1 = ((x1 - x0) * cdx + (y1 - y0) * cdy) * il;
                double t2 = ((x2 - x0) * cdx + (y2 - y0) * cdy) * il;
                double p1x = x0 + t1 * cdx, p1y = y0 + t1 * cdy;
                double p2x = x0 + t2 * cdx, p2y = y0 + t2 * cdy;
                double e1x = x1 - p1x, e1y = y1 - p1y; d1 = e1x * e1x + e1y * e1y;
                double e2x = x2 - p2x, e2y = y2 - p2y; d2 = e2x * e2x + e2y * e2y;
            }
            if (Math.Max(d1, d2) <= FlatTol2) { RenderLine(x0, y0, x3, y3); return; }
            // de Casteljau at t=0.5
            double m01x = (x0 + x1) * .5, m01y = (y0 + y1) * .5;
            double m12x = (x1 + x2) * .5, m12y = (y1 + y2) * .5;
            double m23x = (x2 + x3) * .5, m23y = (y2 + y3) * .5;
            double m012x = (m01x + m12x) * .5, m012y = (m01y + m12y) * .5;
            double m123x = (m12x + m23x) * .5, m123y = (m12y + m23y) * .5;
            double midx = (m012x + m123x) * .5, midy = (m012y + m123y) * .5;
            FlatC(x0, y0, m01x, m01y, m012x, m012y, midx, midy);
            FlatC(midx, midy, m123x, m123y, m23x, m23y, x3, y3);
        }

        // ── RenderLine ────────────────────────────────────────────────────────
        void RenderLine(double wx1, double wy1, double wx2, double wy2)
        {
            if (!double.IsFinite(wx1) || !double.IsFinite(wy1) ||
                !double.IsFinite(wx2) || !double.IsFinite(wy2))
                return;

            if (Math.Abs(wy2 - wy1) < 1e-10) return; // horizontal — skip

            // Sort by Y, determine winding
            double ax, ay, bx, by; int winding;
            if (wy1 <= wy2) { ax = wx1; ay = wy1; bx = wx2; by = wy2; winding = 1; }
            else { ax = wx2; ay = wy2; bx = wx1; by = wy1; winding = -1; }

            // Fixed-point Y extent, clipped to canvas
            int fy1 = Math.Max(ToFixed(ay), rowStart * Sub);
            int fy2 = Math.Min(ToFixed(by), rowEnd * Sub);
            if (fy1 >= fy2) return;

            double slope = (bx - ax) / (by - ay); // world dx per world dy

            int pyTop = fy1 >> SubBits;
            int pyBot = (fy2 - 1) >> SubBits;

            for (int py = pyTop; py <= pyBot; py++)
            {
                int ey0 = Math.Max(fy1, py * Sub);
                int ey1 = Math.Min(fy2, (py + 1) * Sub);
                int dy = ey1 - ey0;
                if (dy <= 0) continue;

                // X in fixed-point at top and bottom of this row's contribution
                int fx1 = ClampFixedX(ax + slope * (ey0 * (1.0 / Sub) - ay), w);
                int fx2 = ClampFixedX(ax + slope * (ey1 * (1.0 / Sub) - ay), w);
                int ry = py - rowStart;

                RenderHLine(
                    fx1, fx2, dy, winding, w,
                    area + ry * w, cover + ry * w, bv + ry * bvWords,
                    ref minWord[ry], ref maxWord[ry]);
            }
        }
    }

    // ── AGG cell accumulation for one pixel row ───────────────────────────────
    //
    // fx1, fx2: fixed-point X at top/bottom of the segment's extent in this row.
    // dy:       sub-pixel height of the contribution (≤ Sub = 256).
    // winding:  +1 or −1.
    //
    // Single column px (ex1 == ex2):
    //   lx1 = fx1 − px×Sub,  lx2 = fx2 − px×Sub  (offsets within pixel [0,Sub])
    //   area[px]  += winding × dy × (lx1 + lx2)
    //   cover[px] += winding × dy
    //
    // Multi-column:
    //   Split at each crossed pixel boundary and distribute dy proportionally.
    //   Each piece contributes area += winding × delta × (localX0 + localX1)
    //   and cover += winding × delta. The integer cumulative splitter guarantees
    //   the deltas sum to dy exactly.
    //
    // Resolve: alpha = FillRule(running × 2×Sub − area[x]) / (2×Sub²)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RenderHLine(
        int fx1, int fx2, int dy, int winding, int w,
        int* area, int* cover, ulong* bv, ref int minWord, ref int maxWord)
    {
        int ex1 = fx1 >> SubBits;
        int ex2 = fx2 >> SubBits;

        if (ex1 == ex2)
        {
            // ── Single pixel column ───────────────────────────────────────────
            if ((uint)ex1 >= (uint)w) return;
            int lx1 = fx1 - ex1 * Sub;
            int lx2 = fx2 - ex1 * Sub;
            area[ex1] += winding * dy * (lx1 + lx2);
            cover[ex1] += winding * dy;
            SetBit(bv, ex1, ref minWord, ref maxWord);
            return;
        }

        if (fx1 < fx2)
        {
            int totalDx = fx2 - fx1;
            int y0 = 0;
            int x0 = fx1;
            long walked = 0;

            while (x0 < fx2)
            {
                int ex = x0 >> SubBits;
                int x1 = Math.Min((ex + 1) * Sub, fx2);
                int pieceDx = x1 - x0;
                walked += pieceDx;

                int y1 = (int)(walked * dy / totalDx);
                int delta = y1 - y0;
                if (delta != 0 && (uint)ex < (uint)w)
                {
                    int lx0 = x0 - ex * Sub;
                    int lx1 = x1 - ex * Sub;
                    area[ex] += winding * delta * (lx0 + lx1);
                    cover[ex] += winding * delta;
                    SetBit(bv, ex, ref minWord, ref maxWord);
                }

                x0 = x1;
                y0 = y1;
            }
        }
        else
        {
            int totalDx = fx1 - fx2;
            int y0 = 0;
            int x0 = fx1;
            long walked = 0;

            while (x0 > fx2)
            {
                int ex = (x0 - 1) >> SubBits;
                int x1 = Math.Max(ex * Sub, fx2);
                int pieceDx = x0 - x1;
                walked += pieceDx;

                int y1 = (int)(walked * dy / totalDx);
                int delta = y1 - y0;
                if (delta != 0 && (uint)ex < (uint)w)
                {
                    int lx0 = x0 - ex * Sub;
                    int lx1 = x1 - ex * Sub;
                    area[ex] += winding * delta * (lx0 + lx1);
                    cover[ex] += winding * delta;
                    SetBit(bv, ex, ref minWord, ref maxWord);
                }

                x0 = x1;
                y0 = y1;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetBit(ulong* bv, int x, ref int minWord, ref int maxWord)
    {
        int wi = x >> 6;
        bv[wi] |= 1UL << (x & 63);
        if (wi < minWord) minWord = wi;
        if (wi > maxWord) maxWord = wi;
    }

    // ── Resolve one scanline → pixel buffer ───────────────────────────────────
    //
    // AGG / Blend2D formula:
    //   running_cover += cover[x]
    //   alpha = FillRule(running_cover × 2×Sub − area[x]) / (2×Sub²)
    //
    // running_cover = Σ cover[j] for j <= x after each cell is merged.
    // Pixels between dirty columns use alpha from running_cover only.
    private void ResolveRow(
        int py, int w, int* area, int* cover, ulong* bv, int minWord, int maxWord,
        double sl, double st, double invW, double invH,
        float opacity, bool nonZero,
        bool solid, Color32 solPre, IBrush fill)
    {
        Color32* row = firstPixel + py * w;
        double sy = Math.Clamp((py + 0.5 - st) * invH, 0.0, 1.0);
        int running = 0;

        int spanStart = minWord * 64;

        for (int wi = minWord; wi <= maxWord; wi++)
        {
            ulong word = bv[wi];
            int base_ = wi * 64;

            while (word != 0)
            {
                int bit = BitOperations.TrailingZeroCount(word);
                word &= word - 1; // clear lowest set bit
                int x = base_ + bit;
                if ((uint)x >= (uint)w) break;

                float spanAlpha = CalculateAlpha((long)running * (2 * Sub), nonZero);
                if (spanAlpha > 1e-5f)
                    BlitSpan(row, spanStart, x, spanAlpha, sy, sl, invW, opacity, solid, solPre, fill, w);

                running += cover[x];

                float alpha = CalculateAlpha((long)running * (2 * Sub) - area[x], nonZero);
                if (alpha > 1e-5f)
                    BlitPixel(row, x, alpha, sy, sl, invW, opacity, solid, solPre, fill);

                spanStart = x + 1;
            }
        }

        float tailAlpha = CalculateAlpha((long)running * (2 * Sub), nonZero);
        if (tailAlpha > 1e-5f)
            BlitSpan(row, spanStart, w, tailAlpha, sy, sl, invW, opacity, solid, solPre, fill, w);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CalculateAlpha(long area, bool nonZero)
    {
        if (area < 0) area = -area;

        if (!nonZero)
        {
            const long period = AreaNorm * 2L;
            area %= period;
            if (area > AreaNorm) area = period - area;
        }

        return area >= AreaNorm ? 1f : area * (1f / AreaNorm);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void BlitPixel(Color32* row, int x, float alpha,
        double sy, double sl, double invW, float opacity,
        bool solid, Color32 solPre, IBrush fill)
    {
        float a = alpha * opacity;
        if (a < 1e-5f) return;
        Vector4 color = solid
            ? (Vector4)solPre * a
            : (Vector4)fill.Sample(Math.Clamp((x + 0.5 - sl) * invW, 0.0, 1.0), sy).ToPremultiplied * a;
        Color32.BlendPremultiplied(color, ref row[x]);
    }

    private void BlitSpan(Color32* row, int x0, int x1, float alpha,
        double sy, double sl, double invW, float opacity,
        bool solid, Color32 solPre, IBrush fill, int w)
    {
        x0 = Math.Max(x0, 0); x1 = Math.Min(x1, w);
        if (x0 >= x1) return;
        float a = alpha * opacity;
        if (a < 1e-5f) return;
        if (solid)
        {
            var c = (Vector4)solPre * a;
            for (int x = x0; x < x1; x++) Color32.BlendPremultiplied(c, ref row[x]);
        }
        else
        {
            for (int x = x0; x < x1; x++)
            {
                double sx = Math.Clamp((x + 0.5 - sl) * invW, 0.0, 1.0);
                Color32.BlendPremultiplied((Vector4)fill.Sample(sx, sy).ToPremultiplied * a, ref row[x]);
            }
        }
    }

    private void StrokePass(
        Path2D path, Pen stroke, float opacity,
        int w, int h, Rect samplingBounds)
    {
        if (!double.IsFinite(stroke.Thickness) || stroke.Thickness <= 0 || path.Count == 0) return;

        double radius = stroke.Thickness * 0.5;
        double dashScale = stroke.Thickness;
        double miterLimit = double.IsFinite(stroke.MiterLimit) && stroke.MiterLimit > 0 ? stroke.MiterLimit : MiterLim;

        DashPattern dash = stroke.DashPattern;
        bool useDash = TryInitializeDash(dash, dashScale, stroke.DashOffset, out int dashIdx, out bool dashOn, out double dashRem);
        var strokeContours = new List<PathContour>(Math.Max(8, path.Count * 8));

        foreach (PathContour contour in path)
        {
            var segs = new List<StrokeSeg>(Math.Max(4, contour.Count * 2));
            int firstValid = -1;
            int lastValid = -1;

            void EmitBody(Point start, Point end, Vector2D tangent)
            {
                Vector2D normal = new(-tangent.Y, tangent.X);
                AddStrokeSegmentContour(strokeContours, start, end, normal, radius);
            }

            void EmitDashPiece(Point start, Point end, Vector2D tangent)
            {
                Vector2D normal = new(-tangent.Y, tangent.X);
                AddStrokeSegmentContour(strokeContours, start, end, normal, radius);
                AddCapContour(strokeContours, start, tangent, normal, radius, stroke.Cap, true);
                AddCapContour(strokeContours, end, tangent, normal, radius, stroke.Cap, false);
            }

            void Emit(double ax, double ay, double bx, double by)
            {
                double dx = bx - ax, dy = by - ay, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-10)
                {
                    AddCircleContour(strokeContours, new Point(ax, ay), radius);
                    segs.Add(new(ax, ay, bx, by, 0, 0, false));
                    return;
                }

                double ux = dx / len, uy = dy / len;
                Point start = new(ax, ay);
                Point end = new(bx, by);
                Vector2D tangent = new(ux, uy);

                if (useDash)
                {
                    double consumed = 0;
                    while (consumed < len)
                    {
                        if (dashRem <= 1e-12)
                        {
                            if (!AdvanceDash(dash, dashScale, ref dashIdx, ref dashOn, ref dashRem))
                                break;
                        }

                        double step = Math.Min(dashRem, len - consumed);
                        if (dashOn)
                        {
                            double t0 = consumed / len, t1 = (consumed + step) / len;
                            Point pieceStart = new(start.X + dx * t0, start.Y + dy * t0);
                            Point pieceEnd = new(start.X + dx * t1, start.Y + dy * t1);
                            EmitDashPiece(pieceStart, pieceEnd, tangent);
                        }

                        consumed += step;
                        dashRem -= step;
                    }
                }
                else
                {
                    EmitBody(start, end, tangent);
                    if (firstValid < 0) firstValid = segs.Count;
                    lastValid = segs.Count;
                }

                segs.Add(new(ax, ay, bx, by, ux, uy, true));
            }

            void FlatQ(double x0, double y0, double x1, double y1, double x2, double y2)
            {
                double mx = (x0 + x2) * .5, my = (y0 + y2) * .5, ex = x1 - mx, ey = y1 - my;
                if (ex * ex + ey * ey <= FlatTol2) { Emit(x0, y0, x2, y2); return; }
                double m01x = (x0 + x1) * .5, m01y = (y0 + y1) * .5;
                double m12x = (x1 + x2) * .5, m12y = (y1 + y2) * .5;
                double mmx = (m01x + m12x) * .5, mmy = (m01y + m12y) * .5;
                FlatQ(x0, y0, m01x, m01y, mmx, mmy);
                FlatQ(mmx, mmy, m12x, m12y, x2, y2);
            }

            void FlatC(double x0, double y0, double x1, double y1,
                       double x2, double y2, double x3, double y3)
            {
                double cdx = x3 - x0, cdy = y3 - y0, cl2 = cdx * cdx + cdy * cdy;
                double d1, d2;
                if (cl2 < 1e-10)
                {
                    double e1x = x1 - x0, e1y = y1 - y0; d1 = e1x * e1x + e1y * e1y;
                    double e2x = x2 - x0, e2y = y2 - y0; d2 = e2x * e2x + e2y * e2y;
                }
                else
                {
                    double il = 1.0 / cl2;
                    double t1 = ((x1 - x0) * cdx + (y1 - y0) * cdy) * il;
                    double t2 = ((x2 - x0) * cdx + (y2 - y0) * cdy) * il;
                    double p1x = x0 + t1 * cdx, p1y = y0 + t1 * cdy;
                    double p2x = x0 + t2 * cdx, p2y = y0 + t2 * cdy;
                    double e1x = x1 - p1x, e1y = y1 - p1y; d1 = e1x * e1x + e1y * e1y;
                    double e2x = x2 - p2x, e2y = y2 - p2y; d2 = e2x * e2x + e2y * e2y;
                }
                if (Math.Max(d1, d2) <= FlatTol2) { Emit(x0, y0, x3, y3); return; }
                double m01x = (x0 + x1) * .5, m01y = (y0 + y1) * .5;
                double m12x = (x1 + x2) * .5, m12y = (y1 + y2) * .5;
                double m23x = (x2 + x3) * .5, m23y = (y2 + y3) * .5;
                double m012x = (m01x + m12x) * .5, m012y = (m01y + m12y) * .5;
                double m123x = (m12x + m23x) * .5, m123y = (m12y + m23y) * .5;
                double midx = (m012x + m123x) * .5, midy = (m012y + m123y) * .5;
                FlatC(x0, y0, m01x, m01y, m012x, m012y, midx, midy);
                FlatC(midx, midy, m123x, m123y, m23x, m23y, x3, y3);
            }

            foreach (IPathSegment seg in contour)
            {
                switch (seg)
                {
                    case LineSegment l:
                        Emit(l.Start.X, l.Start.Y, l.End.X, l.End.Y);
                        break;
                    case QuadraticSegment q:
                        FlatQ(q.Start.X, q.Start.Y, q.Control.X, q.Control.Y, q.End.X, q.End.Y);
                        break;
                    case CubicSegment c:
                        FlatC(c.Start.X, c.Start.Y,
                              c.Control1.X, c.Control1.Y,
                              c.Control2.X, c.Control2.Y,
                              c.End.X, c.End.Y);
                        break;
                }
            }

            if (!useDash && !contour.IsClosed && firstValid >= 0)
            {
                var first = segs[firstValid];
                var last = segs[lastValid];
                AddCapContour(strokeContours, new Point(first.X1, first.Y1), new Vector2D(first.Ux, first.Uy), new Vector2D(-first.Uy, first.Ux), radius, stroke.Cap, true);
                AddCapContour(strokeContours, new Point(last.X2, last.Y2), new Vector2D(last.Ux, last.Uy), new Vector2D(-last.Uy, last.Ux), radius, stroke.Cap, false);
            }

            if (!useDash)
            {
                int joins = contour.IsClosed ? segs.Count : Math.Max(0, segs.Count - 1);
                for (int i = 0; i < joins; i++)
                {
                    var cur = segs[i];
                    var nxt = segs[(i + 1) % segs.Count];
                    if (!cur.Valid || !nxt.Valid) continue;
                    if (!NearlyEq(cur.X2, nxt.X1) || !NearlyEq(cur.Y2, nxt.Y1)) continue;
                    AddJoinContour(strokeContours, new Point(nxt.X1, nxt.Y1), new Vector2D(cur.Ux, cur.Uy), new Vector2D(nxt.Ux, nxt.Uy), radius, stroke.Join, miterLimit);
                }
            }
        }

        if (strokeContours.Count == 0) return;

        Path2D strokePath = new(FillRule.NonZero, strokeContours);
        CellFill(strokePath, stroke.Brush, opacity, w, h, samplingBounds);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryIntersect(
        double x1, double y1, double dx1, double dy1,
        double x2, double y2, double dx2, double dy2,
        out double ix, out double iy)
    {
        double det = dx1 * dy2 - dy1 * dx2;
        if (Math.Abs(det) <= 1e-12) { ix = iy = 0; return false; }
        double t = ((x2 - x1) * dy2 - (y2 - y1) * dx2) / det;
        ix = x1 + dx1 * t; iy = y1 + dy1 * t;
        return double.IsFinite(ix) && double.IsFinite(iy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool NearlyEq(double a, double b) => Math.Abs(a - b) <= 1e-6;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryInitializeDash(
        DashPattern dash, double scale, double offset,
        out int dashIndex, out bool dashOn, out double dashRem)
    {
        dashIndex = 0;
        dashOn = true;
        dashRem = 0;

        int count = dash.Count;
        if (count == 0 || !double.IsFinite(scale) || scale <= 0) return false;

        double cycle = 0;
        for (int i = 0; i < count; i++)
        {
            DashSegment segment = dash[i];
            cycle += Math.Max(0, segment.Fill) + Math.Max(0, segment.Gap);
        }

        if (!(cycle > 0) || !double.IsFinite(offset)) return false;

        double remaining = offset * scale;
        if (!double.IsFinite(remaining)) remaining = 0;
        remaining %= cycle * scale;
        if (remaining < 0) remaining += cycle * scale;
        remaining /= scale;

        for (int guard = 0; guard < count * 4 + 4; guard++)
        {
            DashSegment segment = dash[dashIndex];
            double fill = Math.Max(0, segment.Fill);
            double gap = Math.Max(0, segment.Gap);

            if (remaining < fill)
            {
                dashOn = true;
                dashRem = (fill - remaining) * scale;
                return dashRem > 0;
            }

            remaining -= fill;

            if (remaining < gap)
            {
                dashOn = false;
                dashRem = (gap - remaining) * scale;
                return dashRem > 0;
            }

            remaining -= gap;
            dashIndex = (dashIndex + 1) % count;
        }

        DashSegment first = dash[dashIndex];
        dashOn = true;
        dashRem = Math.Max(0, first.Fill) * scale;
        return dashRem > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AdvanceDash(
        DashPattern dash, double scale,
        ref int dashIndex, ref bool dashOn, ref double dashRem)
    {
        int count = dash.Count;
        if (count == 0 || !double.IsFinite(scale) || scale <= 0) return false;

        for (int guard = 0; guard < count * 4 + 4; guard++)
        {
            if (dashOn)
            {
                dashOn = false;
                dashRem = Math.Max(0, dash[dashIndex].Gap) * scale;
                if (dashRem > 0) return true;
            }
            else
            {
                dashIndex = (dashIndex + 1) % count;
                dashOn = true;
                dashRem = Math.Max(0, dash[dashIndex].Fill) * scale;
                if (dashRem > 0) return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddStrokeSegmentContour(
        List<PathContour> contours,
        Point start, Point end, Vector2D normal, double radius)
    {
        Point p0 = start + normal * radius;
        Point p1 = end + normal * radius;
        Point p2 = end - normal * radius;
        Point p3 = start - normal * radius;
        contours.Add(CreatePolygonContour([p0, p1, p2, p3]));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddCapContour(
        List<PathContour> contours,
        Point endpoint, Vector2D tangent, Vector2D normal,
        double radius, LineCap cap, bool isStart)
    {
        switch (cap)
        {
            case LineCap.Flat:
                return;
            case LineCap.Round:
                AddCircleContour(contours, endpoint, radius);
                return;
            case LineCap.Square:
                Point start = isStart ? endpoint - tangent * radius : endpoint;
                Point end = isStart ? endpoint : endpoint + tangent * radius;
                AddStrokeSegmentContour(contours, start, end, normal, radius);
                return;
            case LineCap.Triangle:
                Point tip = isStart ? endpoint - tangent * radius : endpoint + tangent * radius;
                Point baseLeft = endpoint + normal * radius;
                Point baseRight = endpoint - normal * radius;
                AddTriangleContour(contours, tip, baseLeft, baseRight);
                return;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddJoinContour(
        List<PathContour> contours,
        Point center, Vector2D prevU, Vector2D nextU, double radius, LineJoin join, double miterLimit)
    {
        if (join is LineJoin.None) return;

        double turn = prevU.Cross(nextU);
        if (Math.Abs(turn) <= 1e-10) return;

        Vector2D n0 = new(-prevU.Y, prevU.X);
        Vector2D n1 = new(-nextU.Y, nextU.X);
        if (turn > 0)
        {
            n0 = -n0;
            n1 = -n1;
        }

        Point ax = center + n0 * radius;
        Point bx = center + n1 * radius;
        switch (join)
        {
            case LineJoin.Miter:
                if (TryIntersect(ax.X, ax.Y, prevU.X, prevU.Y, bx.X, bx.Y, nextU.X, nextU.Y, out double mx, out double my))
                {
                    double ml2 = (mx - center.X) * (mx - center.X) + (my - center.Y) * (my - center.Y);
                    if (ml2 <= radius * radius * miterLimit * miterLimit)
                    {
                        contours.Add(CreatePolygonContour([center, ax, new(mx, my), bx]));
                        return;
                    }
                }
                goto case LineJoin.Bevel;
            case LineJoin.Bevel:
                AddTriangleContour(contours, center, ax, bx);
                return;
            case LineJoin.Round:
                AddRoundJoinContour(contours, center, ax, bx);
                return;
        }
    }

    private static void AddRoundJoinContour(List<PathContour> contours, Point center, Point start, Point end)
    {
        double startVectorX = start.X - center.X;
        double startVectorY = start.Y - center.Y;
        double endVectorX = end.X - center.X;
        double endVectorY = end.Y - center.Y;
        double radius = Math.Sqrt(startVectorX * startVectorX + startVectorY * startVectorY);
        if (radius <= 1e-10) return;

        double startAngle = Math.Atan2(startVectorY, startVectorX);
        double endAngle = Math.Atan2(endVectorY, endVectorX);
        double sweep = endAngle - startAngle;
        double fullTurn = Math.PI * 2.0;
        while (sweep <= -Math.PI) sweep += fullTurn;
        while (sweep > Math.PI) sweep -= fullTurn;
        if (Math.Abs(sweep) <= 1e-10) return;

        int pieceCount = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI * 0.5)));
        double pieceSweep = sweep / pieceCount;
        var segments = new List<IPathSegment>(pieceCount + 2)
        {
            new LineSegment(center, start)
        };

        Point current = start;
        double currentAngle = startAngle;
        for (int pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
        {
            double nextAngle = pieceIndex == pieceCount - 1 ? startAngle + sweep : currentAngle + pieceSweep;
            Point next = pieceIndex == pieceCount - 1
                ? end
                : new Point(center.X + Math.Cos(nextAngle) * radius, center.Y + Math.Sin(nextAngle) * radius);
            double cubicK = 4.0 / 3.0 * Math.Tan((nextAngle - currentAngle) * 0.25);
            Point control1 = new(
                current.X - Math.Sin(currentAngle) * radius * cubicK,
                current.Y + Math.Cos(currentAngle) * radius * cubicK);
            Point control2 = new(
                next.X + Math.Sin(nextAngle) * radius * cubicK,
                next.Y - Math.Cos(nextAngle) * radius * cubicK);

            segments.Add(new CubicSegment(current, control1, control2, next));
            current = next;
            currentAngle = nextAngle;
        }

        segments.Add(new LineSegment(end, center));
        contours.Add(new PathContour(segments));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddCircleContour(List<PathContour> contours, Point center, double radius)
    {
        double c = radius * PathBuilder.CircleConstant;
        Point top = new(center.X, center.Y - radius);
        Point right = new(center.X + radius, center.Y);
        Point bot = new(center.X, center.Y + radius);
        Point left = new(center.X - radius, center.Y);

        contours.Add(new PathContour([
            new CubicSegment(top, new(top.X + c, top.Y), new(right.X, right.Y - c), right),
            new CubicSegment(right, new(right.X, right.Y + c), new(bot.X + c, bot.Y), bot),
            new CubicSegment(bot, new(bot.X - c, bot.Y), new(left.X, left.Y + c), left),
            new CubicSegment(left, new(left.X, left.Y - c), new(top.X - c, top.Y), top),
        ]));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddTriangleContour(List<PathContour> contours, Point a, Point b, Point c)
        => contours.Add(CreatePolygonContour([a, b, c]));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PathContour CreatePolygonContour(Point[] points)
    {
        if (points.Length < 2) throw new ArgumentException("Polygon requires at least two points.");
        if (SignedArea(points) < 0) Array.Reverse(points);

        var segs = new IPathSegment[points.Length];
        for (int i = 0; i < points.Length; i++)
            segs[i] = new LineSegment(points[i], points[(i + 1) % points.Length]);
        return new PathContour(segs);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SignedArea(ReadOnlySpan<Point> points)
    {
        double area = 0;
        for (int i = 0; i < points.Length; i++)
        {
            Point a = points[i];
            Point b = points[(i + 1) % points.Length];
            area += a.X * b.Y - b.X * a.Y;
        }
        return area;
    }

    private static Rect? NormalizeRectOrNull(Rect r)
        => IsFiniteRect(r) ? r.Normalized : null;

    private static bool IsFiniteRect(Rect r)
        => double.IsFinite(r.X) && double.IsFinite(r.Y)
        && double.IsFinite(r.Width) && double.IsFinite(r.Height);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ToFixed(double value)
    {
        double fixedValue = Math.Floor(value * Sub);
        if (fixedValue <= int.MinValue) return int.MinValue;
        if (fixedValue >= int.MaxValue) return int.MaxValue;
        return (int)fixedValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ClampFixedX(double x, int w)
    {
        double fixedValue = x * Sub;
        if (fixedValue <= 0) return 0;

        int max = w * Sub;
        if (fixedValue >= max) return max;

        return (int)Math.Floor(fixedValue);
    }

    // ── Types ─────────────────────────────────────────────────────────────────

    private readonly struct StrokeSeg(
        double x1, double y1, double x2, double y2,
        double ux, double uy, bool valid)
    {
        public readonly double X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Ux = ux, Uy = uy;
        public readonly bool Valid = valid;
    }

    internal readonly record struct InheritedPaintState(IBrush? Fill, Pen Stroke, Rect ScopeBounds);
}
