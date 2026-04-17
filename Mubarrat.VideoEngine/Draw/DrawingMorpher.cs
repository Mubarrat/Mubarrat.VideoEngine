using Mubarrat.VideoEngine.Immutable;

namespace Mubarrat.VideoEngine.Draw;

internal static class DrawingMorpher
{
    public static Drawing Lerp(Drawing from, Drawing to, double t)
    {
        var fromNodes = new List<MorphNode>(16);
        var toNodes = new List<MorphNode>(16);

        AppendLeaves(from, Matrix2D.Identity, 1, null, fromNodes);
        AppendLeaves(to, Matrix2D.Identity, 1, null, toNodes);

        if (fromNodes.Count == 0)
            fromNodes.Add(CreatePlaceholderNode(toNodes.Count > 0 ? toNodes[0].Name : string.Empty));
        if (toNodes.Count == 0)
            toNodes.Add(CreatePlaceholderNode(fromNodes.Count > 0 ? fromNodes[0].Name : string.Empty));

        var pairs = new List<(PathDrawing From, PathDrawing To, string Name)>(Math.Max(fromNodes.Count, toNodes.Count));
        bool[] fromUsed = new bool[fromNodes.Count];
        bool[] toUsed = new bool[toNodes.Count];

        var fromNamed = BuildNameBuckets(fromNodes);
        var toNamed = BuildNameBuckets(toNodes);

        foreach (var (name, fromIndices) in fromNamed)
        {
            if (!toNamed.TryGetValue(name, out var toIndices))
                continue;

            fromIndices.Sort((a, b) => CompareByHeuristic(fromNodes[a], fromNodes[b]));
            toIndices.Sort((a, b) => CompareByHeuristic(toNodes[a], toNodes[b]));

            AddPairs(fromNodes, toNodes, fromIndices, toIndices, name, pairs);

            for (int i = 0; i < fromIndices.Count; i++)
                fromUsed[fromIndices[i]] = true;
            for (int i = 0; i < toIndices.Count; i++)
                toUsed[toIndices[i]] = true;
        }

        var fromRemainder = CollectUnused(fromUsed);
        var toRemainder = CollectUnused(toUsed);

        if (fromRemainder.Count > 0 || toRemainder.Count > 0)
        {
            fromRemainder.Sort((a, b) => CompareByHeuristic(fromNodes[a], fromNodes[b]));
            toRemainder.Sort((a, b) => CompareByHeuristic(toNodes[a], toNodes[b]));
            AddPairs(fromNodes, toNodes, fromRemainder, toRemainder, string.Empty, pairs);
        }

        if (pairs.Count == 1)
        {
            var (singleFrom, singleTo, name) = pairs[0];
            var drawing = (PathDrawing)singleFrom.Lerp(singleTo, t);
            drawing.Name = name;
            return drawing;
        }

        var resultDrawings = new List<Drawing>(pairs.Count);
        for (int i = 0; i < pairs.Count; i++)
        {
            var (pairFrom, pairTo, pairName) = pairs[i];
            var morphed = (PathDrawing)pairFrom.Lerp(pairTo, t);
            morphed.Name = pairName;
            resultDrawings.Add(morphed);
        }

        return new GroupDrawing
        {
            Drawings = resultDrawings,
            Transform = Matrix2D.Identity,
            Opacity = 1,
            Name = SelectName(from.Name, to.Name, t)
        };
    }

    private static void AddPairs(
        IReadOnlyList<MorphNode> from,
        IReadOnlyList<MorphNode> to,
        IReadOnlyList<int> fromIndices,
        IReadOnlyList<int> toIndices,
        string bucketName,
        ICollection<(PathDrawing From, PathDrawing To, string Name)> output)
    {
        int fromCount = fromIndices.Count;
        int toCount = toIndices.Count;

        int pairCount = Math.Max(fromCount, toCount);
        if (pairCount == 0)
            return;

        for (int i = 0; i < pairCount; i++)
        {
            int fromIndex = fromCount == 0 ? -1 : fromIndices[(int)((long)i * fromCount / pairCount)];
            int toIndex = toCount == 0 ? -1 : toIndices[(int)((long)i * toCount / pairCount)];

            MorphNode fromNode = fromIndex >= 0 ? from[fromIndex] : CreatePlaceholderNode(bucketName);
            MorphNode toNode = toIndex >= 0 ? to[toIndex] : CreatePlaceholderNode(bucketName);

            string resolvedName = ResolvePairName(fromNode.Name, toNode.Name, bucketName);
            output.Add((fromNode.Drawing, toNode.Drawing, resolvedName));
        }
    }

    private static Dictionary<string, List<int>> BuildNameBuckets(IReadOnlyList<MorphNode> nodes)
    {
        var map = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (int i = 0; i < nodes.Count; i++)
        {
            string name = nodes[i].Name;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!map.TryGetValue(name, out var list))
            {
                list = [];
                map[name] = list;
            }

            list.Add(i);
        }

        return map;
    }

    private static List<int> CollectUnused(IReadOnlyList<bool> used)
    {
        var indices = new List<int>(used.Count);
        for (int i = 0; i < used.Count; i++)
        {
            if (!used[i])
                indices.Add(i);
        }

        return indices;
    }

    private static void AppendLeaves(Drawing drawing, Matrix2D parentTransform, double parentOpacity, string? inheritedName, List<MorphNode> output)
    {
        string? effectiveName = string.IsNullOrWhiteSpace(drawing.Name) ? inheritedName : drawing.Name;

        switch (drawing)
        {
            case PathDrawing path:
            {
                var flattened = new PathDrawing
                {
                    Path = path.Path,
                    Fill = path.Fill,
                    Stroke = path.Stroke,
                    Transform = parentTransform * path.Transform,
                    Opacity = parentOpacity * path.Opacity,
                    Name = effectiveName ?? string.Empty
                };

                Rect bounds = flattened.Bounds.Normalized;
                Point center = bounds.Center;
                if (double.IsNaN(center.X) || double.IsNaN(center.Y))
                    center = Point.Zero;

                double area = bounds.Size.Area;
                if (double.IsNaN(area))
                    area = 0;

                output.Add(new MorphNode(flattened, effectiveName ?? string.Empty, center, area));
                break;
            }

            case GroupDrawing group:
            {
                Matrix2D nextTransform = parentTransform * group.Transform;
                double nextOpacity = parentOpacity * group.Opacity;

                var children = group.Drawings;
                if (children is null || children.Count == 0)
                    return;

                for (int i = 0; i < children.Count; i++)
                    AppendLeaves(children[i], nextTransform, nextOpacity, effectiveName, output);

                break;
            }

            default:
                throw new NotSupportedException($"Unsupported drawing type for morphing: {drawing.GetType().Name}");
        }
    }

    private static MorphNode CreatePlaceholderNode(string? name)
    {
        var drawing = new PathDrawing
        {
            Path = new Path2D(true),
            Fill = null!,
            Stroke = default,
            Transform = Matrix2D.Identity,
            Opacity = 0,
            Name = name ?? string.Empty
        };

        return new MorphNode(drawing, name ?? string.Empty, Point.Zero, 0);
    }

    private static int CompareByHeuristic(in MorphNode a, in MorphNode b)
    {
        int byX = a.Center.X.CompareTo(b.Center.X);
        if (byX != 0) return byX;

        int byY = a.Center.Y.CompareTo(b.Center.Y);
        if (byY != 0) return byY;

        return a.Area.CompareTo(b.Area);
    }

    private static string ResolvePairName(string from, string to, string bucket)
    {
        if (!string.IsNullOrWhiteSpace(bucket))
            return bucket;

        if (!string.IsNullOrWhiteSpace(from) && string.Equals(from, to, StringComparison.Ordinal))
            return from;

        if (!string.IsNullOrWhiteSpace(to))
            return to;

        return from;
    }

    private static string SelectName(string from, string to, double t)
        => !string.IsNullOrWhiteSpace(from) && string.Equals(from, to, StringComparison.Ordinal)
            ? from
            : (t < 0.5 ? from : to);

    private readonly record struct MorphNode(PathDrawing Drawing, string Name, Point Center, double Area);
}
