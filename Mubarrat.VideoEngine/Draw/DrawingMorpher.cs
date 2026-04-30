using Mubarrat.VideoEngine.Immutable;
using System.Runtime.CompilerServices;

namespace Mubarrat.VideoEngine.Draw;

internal static class DrawingMorpher
{
    private readonly record struct MorphCandidate(
        Drawing Drawing,
        Rect Bounds,
        Point Center,
        double Area,
        int Depth,
        string Name,
        bool IsGroup)
    {
        public bool IsNamed => !string.IsNullOrWhiteSpace(Name);
    }

    public static Drawing Lerp(Drawing from, Drawing to, double t)
    {
        var absolute = MorphNodeAbs(from, to, t, new(PairComparer.Instance));
        RelativizeTree(absolute, Matrix2D.Identity);
        return absolute;
    }

    private static Drawing MorphNodeAbs(
        Drawing from,
        Drawing to,
        double t,
        HashSet<(Drawing From, Drawing To)> visited)
    {
        if (!visited.Add((from, to)))
            return t < 0.5 ? (Drawing)from.Clone() : (Drawing)to.Clone();

        if (from is PathDrawing fromPath && to is PathDrawing toPath)
            return MorphPathToPath(fromPath, toPath, t);

        if (from is PathDrawing fromOnlyPath && to is GroupDrawing toGroup)
            return MorphPathToGroup(fromOnlyPath, toGroup, t);

        if (from is GroupDrawing fromGroup && to is PathDrawing toOnlyPath)
            return MorphGroupToPath(fromGroup, toOnlyPath, t);

        if (from is GroupDrawing fromGroup2 && to is GroupDrawing toGroup2)
            return MorphGroupToGroup(fromGroup2, toGroup2, t, visited);

        return MorphFallback(from, to, t);
    }

    private static Drawing MorphPathToPath(PathDrawing from, PathDrawing to, double t) => from.Lerp(to, t);

    private static Drawing MorphPathToGroup(PathDrawing from, GroupDrawing to, double t)
    {
        var clone = (GroupDrawing)to.Clone();
        clone.Transform = to.Transform.Lerp(from.Transform, t);
        clone.Opacity = t * to.Opacity;
        clone.Name = SelectName(from.Name, to.Name, t);

        return t < 0.5
            ? MorphCollapseToCenter(from, to, t)
            : clone;
    }

    private static Drawing MorphGroupToPath(GroupDrawing from, PathDrawing to, double t)
    {
        var clone = (PathDrawing)to.Clone();
        clone.Transform = from.Transform.Lerp(to.Transform, t);
        clone.Opacity = (1 - t) * from.Opacity;
        clone.Name = SelectName(from.Name, to.Name, t);

        return t < 0.5
            ? MorphCollapseToCenter(from, to, t)
            : clone;
    }

    private static Drawing MorphGroupToGroup(
        GroupDrawing from,
        GroupDrawing to,
        double t,
        HashSet<(Drawing From, Drawing To)> visited)
    {
        var resultChildren = new List<Drawing>();

        var fromScope = CollectDirectScopeCandidates(from);
        var toScope = CollectDirectScopeCandidates(to);

        var usedFrom = new HashSet<Drawing>();
        var usedTo = new HashSet<Drawing>();

        var fromNamed = BucketByName(fromScope);
        var toNamed = BucketByName(toScope);

        var commonNames = fromNamed.Keys.Intersect(toNamed.Keys, StringComparer.Ordinal)
            .Select(name => new
            {
                Name = name,
                Depth = Math.Max(MaxDepth(fromNamed[name]), MaxDepth(toNamed[name]))
            })
            .OrderByDescending(x => x.Depth)
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => x.Name)
            .ToList();

        foreach (var name in commonNames)
        {
            var fromNodes = fromNamed[name].Where(n => !usedFrom.Contains(n.Drawing)).ToList();
            var toNodes = toNamed[name].Where(n => !usedTo.Contains(n.Drawing)).ToList();

            if (fromNodes.Count == 0 || toNodes.Count == 0)
                continue;

            MatchNamedNodes(
                name,
                fromNodes,
                toNodes,
                usedFrom,
                usedTo,
                resultChildren,
                t,
                visited);
        }

        foreach (var name in fromNamed.Keys.Except(toNamed.Keys, StringComparer.Ordinal))
        {
            foreach (var node in fromNamed[name])
            {
                if (!usedFrom.Add(node.Drawing))
                    continue;

                resultChildren.Add(CollapseNode(node, t));
            }
        }

        foreach (var name in toNamed.Keys.Except(fromNamed.Keys, StringComparer.Ordinal))
        {
            foreach (var node in toNamed[name])
            {
                if (!usedTo.Add(node.Drawing))
                    continue;

                resultChildren.Add(ExpandNode(node, t));
            }
        }

        var fromUnnamed = fromScope
            .Where(n => string.IsNullOrWhiteSpace(n.Name) && !usedFrom.Contains(n.Drawing))
            .ToList();

        var toUnnamed = toScope
            .Where(n => string.IsNullOrWhiteSpace(n.Name) && !usedTo.Contains(n.Drawing))
            .ToList();

        foreach (var (left, right) in BuildHeuristicPairs(fromUnnamed, toUnnamed))
        {
            usedFrom.Add(left.Drawing);
            usedTo.Add(right.Drawing);
            resultChildren.Add(MorphNodeAbs(left.Drawing, right.Drawing, t, visited));
        }

        var remainingFrom = fromScope.Where(n => !usedFrom.Contains(n.Drawing)).ToList();
        var remainingTo = toScope.Where(n => !usedTo.Contains(n.Drawing)).ToList();

        foreach (var (left, right) in BuildHeuristicPairs(remainingFrom, remainingTo))
        {
            usedFrom.Add(left.Drawing);
            usedTo.Add(right.Drawing);
            resultChildren.Add(MorphNodeAbs(left.Drawing, right.Drawing, t, visited));
        }

        return new GroupDrawing
        {
            Drawings = resultChildren,
            Transform = from.Transform.Lerp(to.Transform, t),
            Opacity = from.Opacity.Lerp(to.Opacity, t),
            Fill = SelectFill(from.Fill, to.Fill, t),
            Stroke = from.Stroke.Lerp(to.Stroke, t),
            Name = SelectName(from.Name, to.Name, t)
        };
    }

    private static List<MorphCandidate> CollectDirectScopeCandidates(GroupDrawing root)
    {
        var list = new List<MorphCandidate>();

        if (root.Drawings is null)
            return list;

        for (int i = 0; i < root.Drawings.Count; i++)
        {
            CollectScopeCandidatesRecursive(
                root.Drawings[i],
                root.Transform,
                1,
                list);
        }

        return list;
    }

    private static Drawing MorphFallback(Drawing from, Drawing to, double t)
    {
        var clone = t < 0.5 ? (Drawing)from.Clone() : (Drawing)to.Clone();
        clone.Transform = from.Transform.Lerp(to.Transform, t);
        clone.Opacity = from.Opacity.Lerp(to.Opacity, t);
        clone.Name = SelectName(from.Name, to.Name, t);
        clone.Fill = SelectFill(from.Fill, to.Fill, t);
        clone.Stroke = from.Stroke.Lerp(to.Stroke, t);
        return clone;
    }

    private static Drawing MorphCollapseToCenter(Drawing from, Drawing to, double t)
    {
        var center = GetCenter(from.Bounds);
        var clone = t < 0.5 ? (Drawing)from.Clone() : (Drawing)to.Clone();
        clone.Transform *= Matrix2D.Scale(1 - t, 1 - t, center);
        clone.Opacity = (1 - t) * from.Opacity;
        return clone;
    }

    private static Drawing ExpandNode(MorphCandidate node, double t)
    {
        var clone = (Drawing)node.Drawing.Clone();
        clone.Transform *= Matrix2D.Scale(t, t, node.Bounds.Center);
        clone.Opacity *= t;
        clone.Name = node.Name;
        return clone;
    }

    private static Drawing CollapseNode(MorphCandidate node, double t)
    {
        var clone = (Drawing)node.Drawing.Clone();
        clone.Transform *= Matrix2D.Scale(1 - t, 1 - t, node.Bounds.Center);
        clone.Opacity *= 1 - t;
        clone.Name = node.Name;
        return clone;
    }

    private static void MatchNamedNodes(
        string name,
        List<MorphCandidate> fromNodes,
        List<MorphCandidate> toNodes,
        HashSet<Drawing> usedFrom,
        HashSet<Drawing> usedTo,
        List<Drawing> result,
        double t,
        HashSet<(Drawing From, Drawing To)> visited)
    {
        fromNodes.Sort(CandidateComparer.Instance);
        toNodes.Sort(CandidateComparer.Instance);

        int fromCount = fromNodes.Count;
        int toCount = toNodes.Count;

        if (fromCount == 0 || toCount == 0)
            return;

        if (fromCount == toCount)
        {
            for (int i = 0; i < fromCount; i++)
            {
                var left = fromNodes[i];
                var right = toNodes[i];
                usedFrom.Add(left.Drawing);
                usedTo.Add(right.Drawing);
                result.Add(MorphNodeAbs(left.Drawing, right.Drawing, t, visited));
            }
            return;
        }

        if (fromCount < toCount)
        {
            var expanded = ExpandIndices(fromCount, toCount);
            for (int i = 0; i < toCount; i++)
            {
                var left = fromNodes[expanded[i]];
                var right = toNodes[i];
                usedFrom.Add(left.Drawing);
                usedTo.Add(right.Drawing);
                result.Add(MorphNodeAbs(left.Drawing, right.Drawing, t, visited));
            }
            return;
        }

        var merged = ExpandIndices(toCount, fromCount);
        for (int i = 0; i < fromCount; i++)
        {
            var left = fromNodes[i];
            var right = toNodes[merged[i]];
            usedFrom.Add(left.Drawing);
            usedTo.Add(right.Drawing);
            result.Add(MorphNodeAbs(left.Drawing, right.Drawing, t, visited));
        }
    }

    private static List<(MorphCandidate From, MorphCandidate To)> BuildHeuristicPairs(
        List<MorphCandidate> fromNodes,
        List<MorphCandidate> toNodes)
    {
        if (fromNodes.Count == 0 && toNodes.Count == 0)
            return [];

        if (fromNodes.Count == 0)
        {
            var list = new List<(MorphCandidate From, MorphCandidate To)>(toNodes.Count);
            for (int i = 0; i < toNodes.Count; i++)
                list.Add((CreatePlaceholder(toNodes[i]), toNodes[i]));
            return list;
        }

        if (toNodes.Count == 0)
        {
            var list = new List<(MorphCandidate From, MorphCandidate To)>(fromNodes.Count);
            for (int i = 0; i < fromNodes.Count; i++)
                list.Add((fromNodes[i], CreatePlaceholder(fromNodes[i])));
            return list;
        }

        fromNodes.Sort(CandidateComparer.Instance);
        toNodes.Sort(CandidateComparer.Instance);

        if (fromNodes.Count == toNodes.Count)
        {
            var list = new List<(MorphCandidate From, MorphCandidate To)>(fromNodes.Count);
            for (int i = 0; i < fromNodes.Count; i++)
                list.Add((fromNodes[i], toNodes[i]));
            return list;
        }

        if (fromNodes.Count < toNodes.Count)
        {
            var expanded = ExpandIndices(fromNodes.Count, toNodes.Count);
            var list = new List<(MorphCandidate From, MorphCandidate To)>(toNodes.Count);
            for (int i = 0; i < toNodes.Count; i++)
                list.Add((fromNodes[expanded[i]], toNodes[i]));
            return list;
        }

        var merged = ExpandIndices(toNodes.Count, fromNodes.Count);
        var result = new List<(MorphCandidate From, MorphCandidate To)>(fromNodes.Count);
        for (int i = 0; i < fromNodes.Count; i++)
            result.Add((fromNodes[i], toNodes[merged[i]]));
        return result;
    }

    private static MorphCandidate CreatePlaceholder(MorphCandidate reference)
    {
        var drawing = new PathDrawing
        {
            Path = new Path2D(true),
            Fill = null!,
            Stroke = default,
            Transform = Matrix2D.Scale(0, 0, reference.Center),
            Opacity = 0,
            Name = reference.Name
        };

        return new MorphCandidate(
            drawing,
            Rect.Empty,
            reference.Center,
            reference.Area,
            reference.Depth,
            reference.Name,
            false);
    }

    private static void CollectScopeCandidatesRecursive(
        Drawing drawing,
        Matrix2D parentAbs,
        int depth,
        List<MorphCandidate> output)
    {
        var abs = drawing.Transform * parentAbs;
        var absClone = CloneAtTransform(drawing, abs);
        var bounds = absClone.Bounds.Normalized;
        var center = GetCenter(bounds);
        var area = GetArea(bounds);
        var name = drawing.Name ?? string.Empty;

        if (drawing is PathDrawing path)
        {
            output.Add(new MorphCandidate(
                absClone,
                bounds,
                center,
                area,
                depth,
                name,
                false));
            return;
        }

        if (drawing is GroupDrawing group)
        {
            if (group.Drawings is { Count: > 0 })
            {
                for (int i = 0; i < group.Drawings.Count; i++)
                    CollectScopeCandidatesRecursive(group.Drawings[i], abs, depth + 1, output);
            }

            if (!string.IsNullOrWhiteSpace(group.Name))
            {
                output.Add(new MorphCandidate(
                    absClone,
                    bounds,
                    center,
                    area,
                    depth,
                    group.Name,
                    true));
            }
        }
    }

    private static Drawing CloneAtTransform(Drawing drawing, Matrix2D transform)
    {
        var clone = (Drawing)drawing.Clone();
        clone.Transform = transform;
        return clone;
    }

    private static Dictionary<string, List<MorphCandidate>> BucketByName(List<MorphCandidate> nodes)
    {
        var map = new Dictionary<string, List<MorphCandidate>>(StringComparer.Ordinal);

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (string.IsNullOrWhiteSpace(node.Name))
                continue;

            if (!map.TryGetValue(node.Name, out var list))
                map[node.Name] = list = [];

            list.Add(node);
        }

        return map;
    }

    private static List<int> ExpandIndices(int count, int target)
    {
        var indices = new List<int>(target);

        for (int i = 0; i < count; i++)
            indices.Add(i);

        while (indices.Count < target)
        {
            int needed = target - indices.Count;
            for (int i = 0; i < needed; i++)
            {
                int sourceIndex = i % count;
                indices.Add(sourceIndex);
            }
        }

        return indices;
    }

    private static int MaxDepth(List<MorphCandidate> nodes)
        => nodes.Count == 0 ? 0 : nodes.Max(n => n.Depth);

    private static Point GetCenter(Rect bounds)
    {
        var center = bounds.Center;
        if (double.IsNaN(center.X) || double.IsNaN(center.Y))
            return Point.Zero;
        return center;
    }

    private static double GetArea(Rect bounds)
    {
        var area = bounds.Size.Area;
        if (double.IsNaN(area))
            return 0;
        return area;
    }

    private static void RelativizeTree(Drawing drawing, Matrix2D parentAbs)
    {
        var abs = drawing.Transform;
        if (parentAbs.IsInvertible)
            drawing.Transform /= parentAbs;
        (drawing as GroupDrawing)?.Drawings.ForEach(child => RelativizeTree(child, abs));
    }

    private static string SelectName(string from, string to, double t)
    {
        if (!string.IsNullOrWhiteSpace(from) && string.Equals(from, to, StringComparison.Ordinal))
            return from;

        return t < 0.5 ? from : to;
    }

    private static IBrush? SelectFill(IBrush? from, IBrush? to, double t) => from?.Lerp(to ?? IBrush.Transparent, t) ?? to?.Lerp(IBrush.Transparent, t) ?? null;

    private sealed class PairComparer : IEqualityComparer<(Drawing From, Drawing To)>
    {
        public static readonly PairComparer Instance = new();

        public bool Equals((Drawing From, Drawing To) x, (Drawing From, Drawing To) y)
            => ReferenceEquals(x.From, y.From) && ReferenceEquals(x.To, y.To);

        public int GetHashCode((Drawing From, Drawing To) obj)
            => HashCode.Combine(RuntimeHelpers.GetHashCode(obj.From), RuntimeHelpers.GetHashCode(obj.To));
    }

    private sealed class CandidateComparer : IComparer<MorphCandidate>
    {
        public static readonly CandidateComparer Instance = new();

        public int Compare(MorphCandidate x, MorphCandidate y)
        {
            int byDepth = y.Depth.CompareTo(x.Depth);
            if (byDepth != 0) return byDepth;

            int byArea = x.Area.CompareTo(y.Area);
            if (byArea != 0) return byArea;

            int byX = x.Center.X.CompareTo(y.Center.X);
            if (byX != 0) return byX;

            return x.Center.Y.CompareTo(y.Center.Y);
        }
    }
}
