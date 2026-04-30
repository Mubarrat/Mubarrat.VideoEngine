using Mubarrat.VideoEngine.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Draw;

public unsafe sealed class DrawingContext(Color32* firstPixel, ushort width, ushort height)
{
    private const double FillSubsampleWeight = 0.5;
    private const double StrokeSampleWeight = 0.25;
    private const double MiterLimit = 4.0;
    private const float FillIntersectionMergeEpsilon = 1e-4f;
    private const float FillHorizontalEdgeEpsilon = 1e-5f;
    private const float FillSubpixelGrid = 4096f;
    private const double FillMinimumSpan = 1e-6;

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
        var (currTransform, currOpacity) = CurrentState;
        stateStack.Push((transform * currTransform, currOpacity * opacity));
    }

    public void Pop() => stateStack.TryPop(out _);

    public void Draw(Drawing drawing)
    {
        if (CurrentState.Opacity == 0 || drawing.Opacity == 0)
            return; // Early exit for better performance when fully transparent
        // (Transform.Determinant == 0) != Invisible, all points map to a line or a point
        switch (drawing)
        {
            case PathDrawing pd:
                var inheritedPaint = CurrentPaint;
                IBrush? effectiveFill = pd.Fill ?? inheritedPaint.Fill;
                Pen effectiveStroke = pd.Stroke.Brush is null ? inheritedPaint.Stroke : pd.Stroke;
                Rect? fillSamplingBounds = pd.Fill is null ? NormalizeRectOrNull(inheritedPaint.ScopeBounds) : null;
                Rect? strokeSamplingBounds = pd.Stroke.Brush is null ? NormalizeRectOrNull(inheritedPaint.ScopeBounds) : null;

                PushState(pd.Transform, pd.Opacity);
                try { DrawPath(pd.Path * CurrentState.Transform, effectiveFill, effectiveStroke, fillSamplingBounds, strokeSamplingBounds); }
                finally { Pop(); }
                break;

            case GroupDrawing gd:
                var parentPaint = CurrentPaint;
                IBrush? groupFill = gd.Fill ?? parentPaint.Fill;
                Pen groupStroke = gd.Stroke.Brush is null ? parentPaint.Stroke : gd.Stroke;
                Rect scopeBounds = ((gd.Bounds * CurrentState.Transform).Normalized);
                if (!IsFiniteRect(scopeBounds))
                    scopeBounds = parentPaint.ScopeBounds;

                paintStack.Push(new InheritedPaintState(groupFill, groupStroke, scopeBounds));
                PushState(gd.Transform, gd.Opacity);
                try { gd.Drawings.ForEach(Draw); }
                finally
                {
                    Pop();
                    paintStack.Pop();
                }
                break;

            default:
                throw new NotImplementedException();
        }
    }

    public void DrawPath(Path2D path, IBrush? fill, Pen stroke, Rect? fillSamplingBounds = null, Rect? strokeSamplingBounds = null)
    {
        if (path.Subpaths is null || path.Subpaths.Length == 0) return;

        var (transform, opacityD) = CurrentState;
        int w = width, h = height;
        if (w == 0 || h == 0) return;

        bool hasFill = fill is not null;
        bool hasStroke = stroke.Thickness > 0 && stroke.Brush is not null;

        double invW = 1.0 / w;
        double invH = 1.0 / h;
        float opacity = (float)opacityD;
        bool fullOpacity = opacity >= 0.999999f;

        // ---------------------
        // 1. Fill (scanline)
        // ---------------------
        // Compute shape bounds in device space and normalize
        var deviceBounds = (path.Bounds * transform).Normalized;
        var effectiveFillBounds = fillSamplingBounds ?? deviceBounds;
        var effectiveStrokeBounds = strokeSamplingBounds ?? deviceBounds;

        if (hasFill)
        {
            FillRasterizer(path, fill!, transform, invW, invH, opacity, fullOpacity, w, h, effectiveFillBounds);
        }

        // ---------------------
        // 2. Stroke
        // ---------------------
        if (hasStroke)
        {
            StrokeRasterizer(path, stroke, transform, invW, invH, opacity, fullOpacity, w, h, effectiveStrokeBounds);
        }
    }

    private void FillRasterizer(Path2D path, IBrush fill, Matrix2D transform, double invW, double invH, float opacity, bool fullOpacity, int w, int h, Rect samplingBounds)
    {
        int maxEdges = 0;
        foreach (var sub in path.Subpaths)
            maxEdges += sub.Edges?.Length ?? 0;

        if (maxEdges == 0)
            return;

        RasterEdge* edges = (RasterEdge*)NativeMemory.Alloc((nuint)maxEdges, (nuint)sizeof(RasterEdge));
        int* next = (int*)NativeMemory.Alloc((nuint)maxEdges, (nuint)sizeof(int));
        int* bucketHeads = (int*)NativeMemory.Alloc((nuint)h, (nuint)sizeof(int));
        int* active = (int*)NativeMemory.Alloc((nuint)maxEdges, (nuint)sizeof(int));
        int* sorted = (int*)NativeMemory.Alloc((nuint)maxEdges, (nuint)sizeof(int));

        try
        {
            // Init buckets
            for (int y = 0; y < h; y++)
                bucketHeads[y] = -1;

            int edgeCount = 0;

            // Build edge table
            foreach (var sub in path.Subpaths)
            {
                var es = sub.Edges;
                if (es is null) continue;

                foreach (var e in es)
                {
                    var (x1d, y1d) = e.Point1;
                    var (x2d, y2d) = e.Point2;

                    if (!double.IsFinite(x1d) || !double.IsFinite(y1d) || !double.IsFinite(x2d) || !double.IsFinite(y2d))
                        continue;

                    float x1 = (float)x1d, y1 = (float)y1d, x2 = (float)x2d, y2 = (float)y2d;

                    float dy = y2 - y1;
                    if (MathF.Abs(dy) <= FillHorizontalEdgeEpsilon)
                        continue;

                    float dx = (x2 - x1) / dy;
                    if (!float.IsFinite(dx))
                        continue;

                    int startY = (int)MathF.Ceiling(MathF.Min(y1, y2) - 0.5f);
                    int endY = (int)MathF.Ceiling(MathF.Max(y1, y2) - 0.5f);

                    if ((uint)startY >= (uint)h) continue;
                    if (endY <= 0) continue;

                    if (startY < 0) startY = 0;
                    if (endY > h) endY = h;

                    if (startY >= endY) continue;

                    float startX = x1 + (startY + 0.5f - y1) * dx;
                    if (!float.IsFinite(startX))
                        continue;

                    int idx = edgeCount++;

                    edges[idx].X = QuantizeFillX(startX);
                    edges[idx].Dx = dx;
                    edges[idx].EndY = endY;
                    edges[idx].Winding = dy > 0 ? 1 : -1;

                    next[idx] = bucketHeads[startY];
                    bucketHeads[startY] = idx;
                }
            }

            int activeCount = 0;

            // Shape-relative sampling: convert device pixel centers to 0..1 within the shape bounds
            double shapeLeft = samplingBounds.Left;
            double shapeTop = samplingBounds.Top;
            double shapeW = samplingBounds.Width;
            double shapeH = samplingBounds.Height;

            double invShapeW = shapeW != 0 ? 1.0 / shapeW : invW;
            double invShapeH = shapeH != 0 ? 1.0 / shapeH : invH;

            double sy = (0.5 - shapeTop) * invShapeH;
            double stepY = invShapeH;

            for (int y = 0; y < h; y++, sy += stepY)
            {
                // Add edges
                for (int ei = bucketHeads[y]; ei != -1; ei = next[ei])
                    active[activeCount++] = ei;

                // Remove finished (swap-remove)
                for (int i = 0; i < activeCount;)
                {
                    int ei = active[i];
                    if (edges[ei].EndY <= y)
                        active[i] = active[--activeCount];
                    else
                        i++;
                }

                if (activeCount < 2)
                {
                    for (int i = 0; i < activeCount; i++)
                        edges[active[i]].X += edges[active[i]].Dx;
                    continue;
                }

                Color32* row = firstPixel + y * w;

                DrawFillScanlineAccum(path.IsNonZeroFill, fill, edges, active, sorted, activeCount,
                    row, y,
                    shapeLeft, shapeTop,
                    invShapeW, invShapeH,
                    opacity, w);

                // Advance edges
                for (int i = 0; i < activeCount; i++)
                    edges[active[i]].X += edges[active[i]].Dx;
            }
        }
        finally
        {
            NativeMemory.Free(edges);
            NativeMemory.Free(next);
            NativeMemory.Free(bucketHeads);
            NativeMemory.Free(active);
            NativeMemory.Free(sorted);
        }
    }

    private void DrawFillScanlineAccum(
    bool isNonZeroFill,
    IBrush fill,
    RasterEdge* edges,
    int* active,
    int* sorted,
    int activeCount,
    Color32* row,
    int y,
    double shapeLeft,
    double shapeTop,
    double invShapeW,
    double invShapeH,
    float opacity,
    int width)
    {
        // 4x MSAA (2x2 grid)
        const int SAMPLE_COUNT = 4;

        Span<float> offsetsX = stackalloc float[] { 0.25f, 0.75f, 0.25f, 0.75f };
        Span<float> offsetsY = stackalloc float[] { 0.25f, 0.25f, 0.75f, 0.75f };

        // Per-pixel accumulators
        Span<float> accumA = stackalloc float[width];
        Span<Vector4> accumC = stackalloc Vector4[width];

        for (int s = 0; s < SAMPLE_COUNT; s++)
        {
            float dx = offsetsX[s] - 0.5f;
            float dy = offsetsY[s] - 0.5f;

            SortActiveEdges(edges, active, sorted, activeCount, dy);

            double sampleY = y + 0.5 + dy;
            double sy = Math.Clamp((sampleY - shapeTop) * invShapeH, 0.0, 1.0);

            int fillState = 0;
            bool inside = false;
            double spanStart = 0;

            for (int i = 0; i < activeCount;)
            {
                int edgeIndex = sorted[i];
                double x = edges[edgeIndex].X + edges[edgeIndex].Dx * dy;

                int delta = 0;
                int crossings = 0;

                int j = i;
                while (j < activeCount)
                {
                    int ei = sorted[j];
                    double gx = edges[ei].X + edges[ei].Dx * dy;

                    if (Math.Abs(gx - x) > FillIntersectionMergeEpsilon)
                        break;

                    if (isNonZeroFill)
                        delta += edges[ei].Winding;
                    else
                        crossings++;

                    j++;
                }

                bool wasInside = inside;

                if (isNonZeroFill)
                {
                    fillState += delta;
                    inside = fillState != 0;
                }
                else
                {
                    fillState += crossings;
                    inside = (fillState & 1) != 0;
                }

                if (!wasInside && inside)
                {
                    spanStart = x;
                }
                else if (wasInside && !inside)
                {
                    AccumulateSpan(
                        fill,
                        accumA,
                        accumC,
                        width,
                        shapeLeft,
                        invShapeW,
                        sy,
                        dx,
                        spanStart,
                        x);
                }

                i = j;
            }
        }

        // 🔥 Final resolve (ONE blend per pixel)
        float weight = 1f / SAMPLE_COUNT;

        for (int x = 0; x < width; x++)
        {
            float a = accumA[x] * weight;
            if (a <= 0) continue;

            a *= opacity;
            if (a <= 0) continue;

            var c = accumC[x] * weight * opacity;

            Color32.BlendPremultiplied(c, ref row[x]);
        }
    }

    private static void AccumulateSpan(
        IBrush fill,
        Span<float> accumA,
        Span<Vector4> accumC,
        int width,
        double shapeLeft,
        double invShapeW,
        double sy,
        double dx,
        double xStart,
        double xEnd)
    {
        if (xEnd <= xStart) return;

        int x0 = Math.Max(0, (int)Math.Floor(xStart));
        int x1 = Math.Min(width - 1, (int)Math.Ceiling(xEnd) - 1);
        if (x0 > x1) return;

        for (int x = x0; x <= x1; x++)
        {
            double coverage = Math.Min(xEnd, x + 1.0) - Math.Max(xStart, x);
            if (coverage <= 0) continue;

            double sx = Math.Clamp((x + 0.5 + dx - shapeLeft) * invShapeW, 0.0, 1.0);

            var color = fill?.Sample(sx, sy).ToPremultiplied ?? default;

            float cov = (float)coverage;

            accumA[x] += cov;
            accumC[x] += (Vector4)color * cov;
        }
    }

    private static void SortActiveEdges(RasterEdge* edges, int* active, int* sorted, int count, float sampleDelta)
    {
        for (int i = 0; i < count; i++)
            sorted[i] = active[i];

        for (int i = 1; i < count; i++)
        {
            int key = sorted[i];
            float keyX = edges[key].X + edges[key].Dx * sampleDelta;

            int j = i - 1;
            while (j >= 0 && edges[sorted[j]].X + edges[sorted[j]].Dx * sampleDelta > keyX)
            {
                sorted[j + 1] = sorted[j];
                j--;
            }

            sorted[j + 1] = key;
        }
    }

    private void DrawFillSample(bool isNonZeroFill, IBrush fill, RasterEdge* edges, int* active, int* sorted, int activeCount,
        Color32* row, int y, double shapeLeft, double shapeTop, double invShapeW, double invShapeH,
        float opacity, int w, float sampleDelta, double sampleWeight)
    {
        SortActiveEdges(edges, active, sorted, activeCount, sampleDelta);

        double sampleY = y + 0.5 + sampleDelta;
        double sy = (sampleY - shapeTop) * invShapeH;
        double syClamped = Math.Clamp(sy, 0.0, 1.0);

        int fillState = 0;
        bool inside = false;
        double spanStart = 0;

        for (int i = 0; i < activeCount;)
        {
            int edgeIndex = sorted[i];
            double x = QuantizeFillX(edges[edgeIndex].X + edges[edgeIndex].Dx * sampleDelta);

            int delta = isNonZeroFill ? 0 : 0;
            int crossings = 0;

            int j = i;
            while (j < activeCount)
            {
                int groupedEdge = sorted[j];
                double gx = QuantizeFillX(edges[groupedEdge].X + edges[groupedEdge].Dx * sampleDelta);
                if (Math.Abs(gx - x) > FillIntersectionMergeEpsilon)
                    break;

                if (isNonZeroFill)
                    delta += edges[groupedEdge].Winding;
                else
                    crossings++;

                j++;
            }

            bool wasInside = inside;

            if (isNonZeroFill)
            {
                fillState += delta;
                inside = fillState != 0;
            }
            else
            {
                fillState += crossings;
                inside = (fillState & 1) != 0;
            }

            if (!wasInside && inside)
            {
                spanStart = x;
            }
            else if (wasInside && !inside)
            {
                DrawFillSpan(fill, row, w, shapeLeft, invShapeW, syClamped, opacity, sampleWeight, spanStart, x);
            }

            i = j;
        }
    }

    private static void DrawFillSpan(IBrush fill, Color32* row, int width, double shapeLeft, double invShapeW,
        double syClamped, float opacity, double sampleWeight, double xStart, double xEnd)
    {
        if (!double.IsFinite(xStart) || !double.IsFinite(xEnd))
            return;

        if (xEnd <= xStart)
            return;

        if (xEnd - xStart <= FillMinimumSpan)
            return;

        int x0 = Math.Max(0, (int)Math.Floor(xStart));
        int x1 = Math.Min(width - 1, (int)Math.Ceiling(xEnd) - 1);
        if (x0 > x1)
            return;

        for (int x = x0; x <= x1; x++)
        {
            double coverage = Math.Min(xEnd, x + 1.0) - Math.Max(xStart, x);
            if (coverage <= 0)
                continue;

            double sx = (x + 0.5 - shapeLeft) * invShapeW;
            double sxClamped = Math.Clamp(sx, 0.0, 1.0);
            double alpha = opacity * sampleWeight * coverage;
            if (alpha <= 0)
                continue;

            Color32.BlendPremultiplied(fill.Sample(sxClamped, syClamped).ToPremultiplied * alpha, ref row[x]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float QuantizeFillX(double x)
        => (float)(Math.Round(x * FillSubpixelGrid) / FillSubpixelGrid);

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
                var segments = new StrokeSegmentInfo[segmentCount];

                for (int i = 0; i < segmentCount; i++)
                {
                    ref readonly var e = ref es[i];
                    var (x1, y1) = e.Point1;
                    var (x2, y2) = e.Point2;

                    double dx = x2 - x1;
                    double dy = y2 - y1;
                    double len = Math.Sqrt(dx * dx + dy * dy);

                    if (len <= 0)
                    {
                        segments[i] = new StrokeSegmentInfo(x1, y1, x2, y2, 0, 0, 0, 0, 0, false);
                        DrawStrokePoint(firstPixel, w, h, x1, y1, radius, r2, stroke, invW, invH, opacity, samplingBounds);
                        continue;
                    }

                    double ux = dx / len;
                    double uy = dy / len;
                    double vx = -uy;
                    double vy = ux;

                    segments[i] = new StrokeSegmentInfo(x1, y1, x2, y2, ux, uy, vx, vy, len, true);

                    DrawStrokeSegment(firstPixel, w, h, x1, y1, x2, y2, ux, uy, vx, vy,
                        len, radius, r2, stroke, stroke.Cap, invW, invH, opacity, samplingBounds);
                }

                bool closed = segmentCount > 1
                    && NearlyEqual(segments[segmentCount - 1].X2, segments[0].X1)
                    && NearlyEqual(segments[segmentCount - 1].Y2, segments[0].Y1);

                int joinCount = closed ? segmentCount : segmentCount - 1;
                for (int i = 0; i < joinCount; i++)
                {
                    int nextIndex = (i + 1) % segmentCount;
                    ref readonly var current = ref segments[i];
                    ref readonly var nextSegment = ref segments[nextIndex];

                    if (!current.IsValid || !nextSegment.IsValid)
                        continue;

                    if (!NearlyEqual(current.X2, nextSegment.X1) || !NearlyEqual(current.Y2, nextSegment.Y1))
                        continue;

                    DrawStrokeJoin(firstPixel, w, h,
                        nextSegment.X1, nextSegment.Y1,
                        current.Ux, current.Uy,
                        nextSegment.Ux, nextSegment.Uy,
                        radius, r2,
                        stroke, stroke.Join,
                        invW, invH, opacity, samplingBounds);
                }

                continue;
            }

            int dashIndex = 0;
            double dashRemaining = dash![0] * stroke.Thickness;
            bool dashOn = true;

            for (int i = 0; i < es.Length; i++)
            {
                ref readonly var e = ref es[i];
                var (x1, y1) = e.Point1;
                var (x2, y2) = e.Point2;

                double dx = x2 - x1;
                double dy = y2 - y1;
                double len = Math.Sqrt(dx * dx + dy * dy);

                if (len <= 0)
                {
                    DrawStrokePoint(firstPixel, w, h, x1, y1, radius, r2, stroke, invW, invH, opacity, samplingBounds);
                    continue;
                }

                double ux = dx / len;
                double uy = dy / len;
                double vx = -uy;
                double vy = ux;

                double consumed = 0;
                while (consumed < len)
                {
                    if (dashRemaining <= 0)
                    {
                        dashIndex = (dashIndex + 1) % dash.Length;
                        dashRemaining = dash[dashIndex] * stroke.Thickness;
                        if (dashRemaining <= 0) continue;
                        dashOn = !dashOn;
                    }

                    double step = Math.Min(dashRemaining, len - consumed);

                    if (dashOn)
                    {
                        double t0 = consumed / len;
                        double t1 = (consumed + step) / len;
                        DrawStrokeSegment(firstPixel, w, h,
                            x1 + dx * t0, y1 + dy * t0, x1 + dx * t1, y1 + dy * t1,
                            ux, uy, vx, vy, step, radius, r2, stroke, stroke.Cap, invW, invH, opacity, samplingBounds);
                    }

                    consumed += step;
                    dashRemaining -= step;
                }
            }
        }
    }

    private void DrawStrokeJoin(Color32* firstPixel, int w, int h,
        double cx, double cy,
        double ux0, double uy0,
        double ux1, double uy1,
        double radius, double r2,
        Pen stroke, LineJoin join,
        double invW, double invH, double opacity, Rect samplingBounds)
    {
        double turn = ux0 * uy1 - uy0 * ux1;
        if (Math.Abs(turn) <= 1e-10)
            return;

        double n0x = -uy0;
        double n0y = ux0;
        double n1x = -uy1;
        double n1y = ux1;

        if (turn < 0)
        {
            n0x = -n0x;
            n0y = -n0y;
            n1x = -n1x;
            n1y = -n1y;
        }

        double ax = cx + n0x * radius;
        double ay = cy + n0y * radius;
        double bx = cx + n1x * radius;
        double by = cy + n1y * radius;

        switch (join)
        {
            case LineJoin.Round:
                DrawRoundJoin(firstPixel, w, h, cx, cy, ax, ay, bx, by, r2, stroke, invW, invH, opacity, samplingBounds);
                return;

            case LineJoin.Miter:
                if (TryIntersectLines(ax, ay, ux0, uy0, bx, by, ux1, uy1, out double mx, out double my))
                {
                    double miterLength = Math.Sqrt((mx - cx) * (mx - cx) + (my - cy) * (my - cy));
                    if (miterLength <= radius * MiterLimit)
                    {
                        DrawTriangleJoin(firstPixel, w, h, ax, ay, mx, my, bx, by, stroke, invW, invH, opacity, samplingBounds);
                        return;
                    }
                }
                break;
        }

        DrawTriangleJoin(firstPixel, w, h, cx, cy, ax, ay, bx, by, stroke, invW, invH, opacity, samplingBounds);
    }

    private void DrawRoundJoin(Color32* firstPixel, int w, int h,
        double cx, double cy,
        double ax, double ay,
        double bx, double by,
        double r2,
        Pen stroke,
        double invW, double invH, double opacity, Rect samplingBounds)
    {
        int minX = Math.Clamp((int)Math.Floor(cx - Math.Sqrt(r2)), 0, w - 1);
        int maxX = Math.Clamp((int)Math.Ceiling(cx + Math.Sqrt(r2)), 0, w - 1);
        int minY = Math.Clamp((int)Math.Floor(cy - Math.Sqrt(r2)), 0, h - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(cy + Math.Sqrt(r2)), 0, h - 1);

        double shapeLeft = samplingBounds.Left;
        double shapeTop = samplingBounds.Top;
        double shapeW = samplingBounds.Width;
        double shapeH = samplingBounds.Height;
        double invShapeW = shapeW != 0 ? 1.0 / shapeW : invW;
        double invShapeH = shapeH != 0 ? 1.0 / shapeH : invH;

        double avx = ax - cx;
        double avy = ay - cy;
        double bvx = bx - cx;
        double bvy = by - cy;
        double crossAB = avx * bvy - avy * bvx;

        for (int py = minY; py <= maxY; py++)
        {
            Color32* row = firstPixel + py * w;
            double cySample = py + 0.5;

            for (int px = minX; px <= maxX; px++)
            {
                int covered = 0;

                covered += IsRoundJoinSampleHit(px + 0.25, py + 0.25, cx, cy, r2, avx, avy, bvx, bvy, crossAB) ? 1 : 0;
                covered += IsRoundJoinSampleHit(px + 0.75, py + 0.25, cx, cy, r2, avx, avy, bvx, bvy, crossAB) ? 1 : 0;
                covered += IsRoundJoinSampleHit(px + 0.25, py + 0.75, cx, cy, r2, avx, avy, bvx, bvy, crossAB) ? 1 : 0;
                covered += IsRoundJoinSampleHit(px + 0.75, py + 0.75, cx, cy, r2, avx, avy, bvx, bvy, crossAB) ? 1 : 0;

                if (covered <= 0)
                    continue;

                double sx = Math.Clamp((px + 0.5 - shapeLeft) * invShapeW, 0.0, 1.0);
                double sy = Math.Clamp((cySample - shapeTop) * invShapeH, 0.0, 1.0);
                double alpha = covered * StrokeSampleWeight * opacity;
                Color32.BlendPremultiplied(stroke.Sample(sx, sy).ToPremultiplied * alpha, ref row[px]);
            }
        }
    }

    private void DrawTriangleJoin(Color32* firstPixel, int w, int h,
        double x1, double y1,
        double x2, double y2,
        double x3, double y3,
        Pen stroke,
        double invW, double invH, double opacity, Rect samplingBounds)
    {
        int minX = Math.Clamp((int)Math.Floor(Math.Min(x1, Math.Min(x2, x3))), 0, w - 1);
        int maxX = Math.Clamp((int)Math.Ceiling(Math.Max(x1, Math.Max(x2, x3))), 0, w - 1);
        int minY = Math.Clamp((int)Math.Floor(Math.Min(y1, Math.Min(y2, y3))), 0, h - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(Math.Max(y1, Math.Max(y2, y3))), 0, h - 1);

        double shapeLeft = samplingBounds.Left;
        double shapeTop = samplingBounds.Top;
        double shapeW = samplingBounds.Width;
        double shapeH = samplingBounds.Height;
        double invShapeW = shapeW != 0 ? 1.0 / shapeW : invW;
        double invShapeH = shapeH != 0 ? 1.0 / shapeH : invH;

        for (int py = minY; py <= maxY; py++)
        {
            Color32* row = firstPixel + py * w;
            double cy = py + 0.5;

            for (int px = minX; px <= maxX; px++)
            {
                int covered = 0;

                covered += IsPointInTriangle(px + 0.25, py + 0.25, x1, y1, x2, y2, x3, y3) ? 1 : 0;
                covered += IsPointInTriangle(px + 0.75, py + 0.25, x1, y1, x2, y2, x3, y3) ? 1 : 0;
                covered += IsPointInTriangle(px + 0.25, py + 0.75, x1, y1, x2, y2, x3, y3) ? 1 : 0;
                covered += IsPointInTriangle(px + 0.75, py + 0.75, x1, y1, x2, y2, x3, y3) ? 1 : 0;

                if (covered <= 0)
                    continue;

                double sx = Math.Clamp((px + 0.5 - shapeLeft) * invShapeW, 0.0, 1.0);
                double sy = Math.Clamp((cy - shapeTop) * invShapeH, 0.0, 1.0);
                double alpha = covered * StrokeSampleWeight * opacity;
                Color32.BlendPremultiplied(stroke.Sample(sx, sy).ToPremultiplied * alpha, ref row[px]);
            }
        }
    }

    private static bool IsRoundJoinSampleHit(double x, double y,
        double cx, double cy, double r2,
        double avx, double avy,
        double bvx, double bvy,
        double crossAB)
    {
        double vx = x - cx;
        double vy = y - cy;

        if (vx * vx + vy * vy > r2)
            return false;

        double crossA = avx * vy - avy * vx;
        double crossB = vx * bvy - vy * bvx;

        return crossAB >= 0
            ? crossA >= 0 && crossB >= 0
            : crossA <= 0 && crossB <= 0;
    }

    private static bool IsPointInTriangle(double px, double py,
        double x1, double y1,
        double x2, double y2,
        double x3, double y3)
    {
        double d1 = Sign(px, py, x1, y1, x2, y2);
        double d2 = Sign(px, py, x2, y2, x3, y3);
        double d3 = Sign(px, py, x3, y3, x1, y1);

        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    private static double Sign(double px, double py, double x1, double y1, double x2, double y2)
        => (px - x2) * (y1 - y2) - (x1 - x2) * (py - y2);

    private static bool TryIntersectLines(double x1, double y1, double dx1, double dy1,
        double x2, double y2, double dx2, double dy2,
        out double ix, out double iy)
    {
        double det = dx1 * dy2 - dy1 * dx2;
        if (Math.Abs(det) <= 1e-12)
        {
            ix = 0;
            iy = 0;
            return false;
        }

        double sx = x2 - x1;
        double sy = y2 - y1;
        double t = (sx * dy2 - sy * dx2) / det;
        ix = x1 + dx1 * t;
        iy = y1 + dy1 * t;
        return double.IsFinite(ix) && double.IsFinite(iy);
    }

    private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) <= 1e-6;

    private readonly struct StrokeSegmentInfo(
        double x1, double y1,
        double x2, double y2,
        double ux, double uy,
        double vx, double vy,
        double len,
        bool isValid)
    {
        public readonly double X1 = x1;
        public readonly double Y1 = y1;
        public readonly double X2 = x2;
        public readonly double Y2 = y2;
        public readonly double Ux = ux;
        public readonly double Uy = uy;
        public readonly double Vx = vx;
        public readonly double Vy = vy;
        public readonly double Len = len;
        public readonly bool IsValid = isValid;
    }

    private struct RasterEdge { public float X, Dx; public int EndY, Winding; }

    private void DrawStrokePoint(Color32* firstPixel, int w, int h, double x, double y, double radius, double r2, Pen stroke, double invW, double invH, double opacity, Rect samplingBounds)
    {
        int minX = Math.Clamp((int)(x - radius), 0, w - 1);
        int maxX = Math.Clamp((int)(x + radius), 0, w - 1);
        int minY = Math.Clamp((int)(y - radius), 0, h - 1);
        int maxY = Math.Clamp((int)(y + radius), 0, h - 1);

        double shapeLeft = samplingBounds.Left;
        double shapeTop = samplingBounds.Top;
        double shapeW = samplingBounds.Width;
        double shapeH = samplingBounds.Height;

        double invShapeW = shapeW != 0 ? 1.0 / shapeW : invW;
        double invShapeH = shapeH != 0 ? 1.0 / shapeH : invH;

        for (int py = minY; py <= maxY; py++)
        {
            Color32* row = firstPixel + py * w;
            double cy = py + 0.5;
            for (int px = minX; px <= maxX; px++)
            {
                int covered = 0;

                double sx0 = px + 0.25;
                double sx1 = px + 0.75;
                double sy0 = py + 0.25;
                double sy1 = py + 0.75;

                double dx = sx0 - x;
                double dy = sy0 - y;
                if (dx * dx + dy * dy <= r2) covered++;

                dx = sx1 - x;
                dy = sy0 - y;
                if (dx * dx + dy * dy <= r2) covered++;

                dx = sx0 - x;
                dy = sy1 - y;
                if (dx * dx + dy * dy <= r2) covered++;

                dx = sx1 - x;
                dy = sy1 - y;
                if (dx * dx + dy * dy <= r2) covered++;

                if (covered > 0)
                {
                    double sx = (px + 0.5 - shapeLeft) * invShapeW;
                    double sy = (cy - shapeTop) * invShapeH;
                    double coverage = covered * StrokeSampleWeight;
                    Color32.BlendPremultiplied(stroke.Sample(Math.Clamp(sx, 0.0, 1.0), Math.Clamp(sy, 0.0, 1.0)).ToPremultiplied * (opacity * coverage), ref row[px]);
                }
            }
        }
    }

    private void DrawStrokeSegment(Color32* firstPixel, int w, int h, double x1, double y1, double x2, double y2,
        double ux, double uy, double vx, double vy, double len, double radius, double r2,
        Pen stroke, LineCap cap, double invW, double invH, double opacity, Rect samplingBounds)
    {
        double pad = cap == LineCap.Flat ? 0 : radius;
        int minX = Math.Clamp((int)Math.Floor(Math.Min(x1, x2) - pad - radius), 0, w - 1);
        int maxX = Math.Clamp((int)Math.Ceiling(Math.Max(x1, x2) + pad + radius), 0, w - 1);
        int minY = Math.Clamp((int)Math.Floor(Math.Min(y1, y2) - pad - radius), 0, h - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(Math.Max(y1, y2) + pad + radius), 0, h - 1);

        double shapeLeft = samplingBounds.Left;
        double shapeTop = samplingBounds.Top;
        double shapeW = samplingBounds.Width;
        double shapeH = samplingBounds.Height;

        double invShapeW = shapeW != 0 ? 1.0 / shapeW : invW;
        double invShapeH = shapeH != 0 ? 1.0 / shapeH : invH;

        for (int py = minY; py <= maxY; py++)
        {
            Color32* row = firstPixel + py * w;
            double cy = py + 0.5;

            for (int px = minX; px <= maxX; px++)
            {
                int covered = 0;

                covered += IsStrokeSampleHit(px + 0.25, py + 0.25, x1, y1, x2, y2, ux, uy, vx, vy, len, radius, r2, cap) ? 1 : 0;
                covered += IsStrokeSampleHit(px + 0.75, py + 0.25, x1, y1, x2, y2, ux, uy, vx, vy, len, radius, r2, cap) ? 1 : 0;
                covered += IsStrokeSampleHit(px + 0.25, py + 0.75, x1, y1, x2, y2, ux, uy, vx, vy, len, radius, r2, cap) ? 1 : 0;
                covered += IsStrokeSampleHit(px + 0.75, py + 0.75, x1, y1, x2, y2, ux, uy, vx, vy, len, radius, r2, cap) ? 1 : 0;

                if (covered > 0)
                {
                    double sx = (px + 0.5 - shapeLeft) * invShapeW;
                    double sy = (cy - shapeTop) * invShapeH;
                    double coverage = covered * StrokeSampleWeight;
                    Color32.BlendPremultiplied(stroke.Sample(Math.Clamp(sx, 0.0, 1.0), Math.Clamp(sy, 0.0, 1.0)).ToPremultiplied * (opacity * coverage), ref row[px]);
                }
            }
        }
    }

    private static bool IsStrokeSampleHit(double sampleX, double sampleY, double x1, double y1, double x2, double y2,
        double ux, double uy, double vx, double vy, double len, double radius, double r2, LineCap cap)
    {
        double rx = sampleX - x1;
        double ry = sampleY - y1;
        double along = rx * ux + ry * uy;
        double across = rx * vx + ry * vy;

        return cap switch
        {
            LineCap.Flat => along >= 0 && along <= len && Math.Abs(across) <= radius,
            LineCap.Square => along >= -radius && along <= len + radius && Math.Abs(across) <= radius,
            LineCap.Round => (along >= 0 && along <= len && Math.Abs(across) <= radius)
                             || (along < 0 && rx * rx + ry * ry <= r2)
                             || (along > len && (sampleX - x2) * (sampleX - x2) + (sampleY - y2) * (sampleY - y2) <= r2),
            _ => along >= 0 && along <= len && Math.Abs(across) <= radius
        };
    }

    private static Rect? NormalizeRectOrNull(Rect rect)
    {
        if (!IsFiniteRect(rect))
            return null;

        return rect.Normalized;
    }

    private static bool IsFiniteRect(Rect rect)
        => double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.Width) && double.IsFinite(rect.Height);

    private readonly record struct InheritedPaintState(IBrush? Fill, Pen Stroke, Rect ScopeBounds);
}
