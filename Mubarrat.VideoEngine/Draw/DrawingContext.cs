using Mubarrat.VideoEngine.Immutable;
using System.Runtime.InteropServices;

namespace Mubarrat.VideoEngine.Draw;

public unsafe sealed class DrawingContext(Color32* firstPixel, ushort width, ushort height)
{
    private readonly Stack<(Matrix2D Transform, double Opacity)> stateStack = new();

    private (Matrix2D Transform, double Opacity) CurrentState =>
        stateStack.Count > 0 ? stateStack.Peek() : (Matrix2D.Identity, 1);

    public void PushTransform(Matrix2D transform) => PushState(transform, 1);
    public void PushOpacity(double opacity) => PushState(Matrix2D.Identity, opacity);

    public void PushState(Matrix2D transform, double opacity)
    {
        opacity = double.Clamp(opacity, 0, 1);
        var (currTransform, currOpacity) = CurrentState;
        stateStack.Push((currTransform * transform, currOpacity * opacity));
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
                PushState(pd.Transform, pd.Opacity);
                try { DrawPath(pd.Path, pd.Fill, pd.Stroke); }
                finally { Pop(); }
                break;

            case GroupDrawing gd:
                PushState(gd.Transform, gd.Opacity);
                try { gd.Drawings.ForEach(Draw); }
                finally { Pop(); }
                break;

            default:
                throw new NotImplementedException();
        }
    }

    public void DrawPath(Path2D path, IBrush fill, Pen stroke)
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

        if (hasFill)
        {
            FillRasterizer(path, fill!, transform, invW, invH, opacity, fullOpacity, w, h, deviceBounds);
        }

        // ---------------------
        // 2. Stroke
        // ---------------------
        if (hasStroke)
        {
            StrokeRasterizer(path, stroke, transform, invW, invH, opacity, fullOpacity, w, h, deviceBounds);
        }
    }

    private void FillRasterizer(Path2D path, IBrush fill, Matrix2D transform, double invW, double invH, float opacity, bool fullOpacity, int w, int h, Rect deviceBounds)
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
                    var (x1d, y1d) = e.Point1 * transform;
                    var (x2d, y2d) = e.Point2 * transform;

                    if (y1d == y2d) continue;

                    float x1 = (float)x1d, y1 = (float)y1d, x2 = (float)x2d, y2 = (float)y2d;

                    float dy = y2 - y1, dx = (x2 - x1) / dy;

                    int startY = (int)MathF.Ceiling(MathF.Min(y1, y2) - 0.5f);
                    int endY = (int)MathF.Ceiling(MathF.Max(y1, y2) - 0.5f);

                    if ((uint)startY >= (uint)h) continue;
                    if (endY <= 0) continue;

                    if (startY < 0) startY = 0;
                    if (endY > h) endY = h;

                    if (startY >= endY) continue;

                    int idx = edgeCount++;

                    edges[idx].X = x1 + (startY + 0.5f - y1) * dx;
                    edges[idx].Dx = dx;
                    edges[idx].EndY = endY;
                    edges[idx].Winding = dy > 0 ? 1 : -1;

                    next[idx] = bucketHeads[startY];
                    bucketHeads[startY] = idx;
                }
            }

            int activeCount = 0;

            // Shape-relative sampling: convert device pixel centers to 0..1 within the shape bounds
            double shapeLeft = deviceBounds.Left;
            double shapeTop = deviceBounds.Top;
            double shapeW = deviceBounds.Width;
            double shapeH = deviceBounds.Height;

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

                // Incremental sort (nearly sorted)
                for (int i = 1; i < activeCount; i++)
                {
                    if (edges[active[i - 1]].X > edges[active[i]].X)
                    {
                        (active[i - 1], active[i]) = (active[i], active[i - 1]);
                        int j = i - 1;
                        while (j > 0 && edges[active[j - 1]].X > edges[active[j]].X)
                        {
                            (active[j - 1], active[j]) = (active[j], active[j - 1]);
                            j--;
                        }
                    }
                }

                Color32* row = firstPixel + y * w;

                int winding = 0;

                for (int i = 0; i + 1 < activeCount; i++)
                {
                    winding += edges[active[i]].Winding;
                    if (path.IsNonZeroFill && winding == 0)
                        continue;

                    float xStart = edges[active[i]].X;
                    float xEnd = edges[active[i + 1]].X;

                    if (xEnd <= xStart)
                        continue;

                    int x0 = (int)(xStart + 0.5f);
                    int x1 = (int)(xEnd - 0.5f);

                    if (x0 < 0) x0 = 0;
                    if (x1 >= w) x1 = w - 1;
                    if (x0 > x1) continue;

                    Color32* p = row + x0;
                    double sx = (x0 + 0.5 - shapeLeft) * invShapeW;
                    if (fullOpacity)
                        for (int x = x0; x <= x1; x++, p++, sx += invShapeW)
                            Color32.BlendPremultiplied(fill.Sample(Math.Clamp(sx, 0.0, 1.0), Math.Clamp(sy, 0.0, 1.0)).ToPremultiplied, ref *p);
                    else
                        for (int x = x0; x <= x1; x++, p++, sx += invShapeW)
                            Color32.BlendPremultiplied(fill.Sample(Math.Clamp(sx, 0.0, 1.0), Math.Clamp(sy, 0.0, 1.0)).ToPremultiplied * opacity, ref *p);
                }

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
        }
    }

    private void StrokeRasterizer(Path2D path, Pen stroke, Matrix2D transform,
        double invW, double invH, float opacity, bool fullOpacity, int w, int h, Rect deviceBounds)
    {
        double radius = stroke.Thickness * 0.5;
        double r2 = radius * radius;

        foreach (var sub in path.Subpaths)
        {
            var es = sub.Edges;
            if (es is null || es.Length == 0) continue;

            double[]? dash = stroke.DashPattern;
            bool useDash = dash is { Length: > 0 };

            int dashIndex = 0;
            double dashRemaining = useDash ? dash![0] * stroke.Thickness : 0;
            bool dashOn = true;

            for (int i = 0; i < es.Length; i++)
            {
                ref readonly var e = ref es[i];
                var (x1, y1) = e.Point1 * transform;
                var (x2, y2) = e.Point2 * transform;

                double dx = x2 - x1;
                double dy = y2 - y1;
                double len = Math.Sqrt(dx * dx + dy * dy);

                if (len <= 0)
                {
                    DrawStrokePoint(firstPixel, w, h, x1, y1, radius, r2, stroke, invW, invH, opacity, deviceBounds);
                    continue;
                }

                double ux = dx / len;
                double uy = dy / len;
                double vx = -uy;
                double vy = ux;

                if (!useDash)
                {
                    DrawStrokeSegment(firstPixel, w, h, x1, y1, x2, y2, ux, uy, vx, vy,
                    len, radius, r2, stroke, stroke.Cap, invW, invH, opacity, deviceBounds);
                    continue;
                }

                double consumed = 0;
                while (consumed < len)
                {
                    if (dashRemaining <= 0)
                    {
                        dashIndex = (dashIndex + 1) % dash!.Length;
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
                            ux, uy, vx, vy, step, radius, r2, stroke, stroke.Cap, invW, invH, opacity, deviceBounds);
                    }

                    consumed += step;
                    dashRemaining -= step;
                }
            }
        }
    }

    private struct RasterEdge { public float X, Dx; public int EndY, Winding; }

    private void DrawStrokePoint(Color32* firstPixel, int w, int h, double x, double y, double radius, double r2, Pen stroke, double invW, double invH, double opacity, Rect deviceBounds)
    {
        int minX = Math.Clamp((int)(x - radius), 0, w - 1);
        int maxX = Math.Clamp((int)(x + radius), 0, w - 1);
        int minY = Math.Clamp((int)(y - radius), 0, h - 1);
        int maxY = Math.Clamp((int)(y + radius), 0, h - 1);

        double shapeLeft = deviceBounds.Left;
        double shapeTop = deviceBounds.Top;
        double shapeW = deviceBounds.Width;
        double shapeH = deviceBounds.Height;

        double invShapeW = shapeW != 0 ? 1.0 / shapeW : invW;
        double invShapeH = shapeH != 0 ? 1.0 / shapeH : invH;

        for (int py = minY; py <= maxY; py++)
        {
            Color32* row = firstPixel + py * w;
            double cy = py + 0.5;
            double dy = cy - y;
            double dy2 = dy * dy;
            for (int px = minX; px <= maxX; px++)
            {
                double dx = px + 0.5 - x;
                if (dx * dx + dy2 <= r2)
                {
                    double sx = (px + 0.5 - shapeLeft) * invShapeW;
                    double sy = (cy - shapeTop) * invShapeH;
                    Color32.BlendPremultiplied(stroke.Sample(Math.Clamp(sx, 0.0, 1.0), Math.Clamp(sy, 0.0, 1.0)).ToPremultiplied * opacity, ref row[px]);
                }
            }
        }
    }

    private void DrawStrokeSegment(Color32* firstPixel, int w, int h, double x1, double y1, double x2, double y2,
        double ux, double uy, double vx, double vy, double len, double radius, double r2,
        Pen stroke, LineCap cap, double invW, double invH, double opacity, Rect deviceBounds)
    {
        double pad = cap == LineCap.Flat ? 0 : radius;
        int minX = Math.Clamp((int)Math.Floor(Math.Min(x1, x2) - pad - radius), 0, w - 1);
        int maxX = Math.Clamp((int)Math.Ceiling(Math.Max(x1, x2) + pad + radius), 0, w - 1);
        int minY = Math.Clamp((int)Math.Floor(Math.Min(y1, y2) - pad - radius), 0, h - 1);
        int maxY = Math.Clamp((int)Math.Ceiling(Math.Max(y1, y2) + pad + radius), 0, h - 1);

        double shapeLeft = deviceBounds.Left;
        double shapeTop = deviceBounds.Top;
        double shapeW = deviceBounds.Width;
        double shapeH = deviceBounds.Height;

        double invShapeW = shapeW != 0 ? 1.0 / shapeW : invW;
        double invShapeH = shapeH != 0 ? 1.0 / shapeH : invH;

        for (int py = minY; py <= maxY; py++)
        {
            Color32* row = firstPixel + py * w;
            double cy = py + 0.5;
            double ry = cy - y1;
            double ry2 = ry * ry;

            for (int px = minX; px <= maxX; px++)
            {
                double rx = px + 0.5 - x1;
                double along = rx * ux + ry * uy;
                double across = rx * vx + ry * vy;

                bool hit = cap switch
                {
                    LineCap.Flat => along >= 0 && along <= len && Math.Abs(across) <= radius,
                    LineCap.Square => along >= -radius && along <= len + radius && Math.Abs(across) <= radius,
                    LineCap.Round => (along >= 0 && along <= len && Math.Abs(across) <= radius)
                                     || (along < 0 && rx * rx + ry2 <= r2)
                                     || (along > len && (px + 0.5 - x2) * (px + 0.5 - x2) + (cy - y2) * (cy - y2) <= r2),
                    _ => along >= 0 && along <= len && Math.Abs(across) <= radius
                };

                if (hit)
                {
                    double sx = (px + 0.5 - shapeLeft) * invShapeW;
                    double sy = (cy - shapeTop) * invShapeH;
                    Color32.BlendPremultiplied(stroke.Sample(Math.Clamp(sx, 0.0, 1.0), Math.Clamp(sy, 0.0, 1.0)).ToPremultiplied * opacity, ref row[px]);
                }
            }
        }
    }
}
