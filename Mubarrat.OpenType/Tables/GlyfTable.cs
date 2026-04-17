using System.Numerics;

namespace Mubarrat.OpenType.Tables;

public sealed class GlyfTable : IOpenTypeTable
{
    public string Tag => "glyf";

    public Glyph[] Glyphs { get; private set; } = [];

    private HmtxTable? hmtx;
    private VmtxTable? vmtx;

    private Vector2[][]? resolvedPoints;
    private byte[]? resolvePointsState;
    private int[]? resolvePointsOwnerThread;
    private Vector2[][]? resolvedAlignmentPoints;
    private byte[]? resolveAlignmentState;
    private int[]? resolveAlignmentOwnerThread;
    private GlyphOutlineContour[][]? resolvedContours;
    private byte[]? resolveContourState;
    private int[]? resolveContourOwnerThread;

    public abstract record GlyphDescription;

    public sealed record SimpleGlyphDescription(
        ushort[] EndPtsOfContours,
        byte[] Instructions,
        SimpleGlyphFlags[] Flags,
        short[] XCoordinates,
        short[] YCoordinates) : GlyphDescription;

    [Flags]
    public enum SimpleGlyphFlags : byte
    {
        ON_CURVE_POINT = 0x01,
        X_SHORT_VECTOR = 0x02,
        Y_SHORT_VECTOR = 0x04,
        REPEAT_FLAG = 0x08,
        X_IS_SAME_OR_POSITIVE_X_SHORT_VECTOR = 0x10,
        Y_IS_SAME_OR_POSITIVE_Y_SHORT_VECTOR = 0x20,
        OVERLAP_SIMPLE = 0x40,
        RESERVED = 0x80
    }

    public sealed record CompositeGlyphDescription(
        CompositeComponent[] Components,
        byte[] Instructions) : GlyphDescription;

    public sealed record CompositeComponent(
        CompositeGlyphFlags Flags,
        ushort GlyphIndex,
        int Arg1,
        int Arg2,
        Matrix3x2 Transform);

    [Flags]
    public enum CompositeGlyphFlags : ushort
    {
        ARG_1_AND_2_ARE_WORDS = 0x0001,
        ARGS_ARE_XY_VALUES = 0x0002,
        ROUND_XY_TO_GRID = 0x0004,
        WE_HAVE_A_SCALE = 0x0008,
        MORE_COMPONENTS = 0x0020,
        WE_HAVE_AN_X_AND_Y_SCALE = 0x0040,
        WE_HAVE_A_TWO_BY_TWO = 0x0080,
        WE_HAVE_INSTRUCTIONS = 0x0100,
        USE_MY_METRICS = 0x0200,
        OVERLAP_COMPOUND = 0x0400,
        SCALED_COMPONENT_OFFSET = 0x0800,
        UNSCALED_COMPONENT_OFFSET = 0x1000,
        RESERVED = 0xE010
    }

    public sealed record Glyph(
        short NumberOfContours,
        short XMin,
        short YMin,
        short XMax,
        short YMax,
        GlyphDescription? Description);

    public readonly record struct GlyphOutlinePoint(Vector2 Position, bool OnCurve);

    public sealed record GlyphOutlineContour(GlyphOutlinePoint[] Points);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        tables.Request<MaxpTable, LocaTable>((maxp, loca, scope) =>
        {
            tables.TryGet(out hmtx);
            tables.TryGet(out vmtx);

            var glyphs = new Glyph[maxp.NumGlyphs];
            resolvedPoints = new Vector2[maxp.NumGlyphs][];
            resolvePointsState = new byte[maxp.NumGlyphs];
            resolvePointsOwnerThread = new int[maxp.NumGlyphs];
            resolvedAlignmentPoints = new Vector2[maxp.NumGlyphs][];
            resolveAlignmentState = new byte[maxp.NumGlyphs];
            resolveAlignmentOwnerThread = new int[maxp.NumGlyphs];
            resolvedContours = new GlyphOutlineContour[maxp.NumGlyphs][];
            resolveContourState = new byte[maxp.NumGlyphs];
            resolveContourOwnerThread = new int[maxp.NumGlyphs];

            for (int i = 0; i < maxp.NumGlyphs; i++)
            {
                int start = (int)loca.Offsets[i];
                int end = (int)loca.Offsets[i + 1];

                if (start == end)
                {
                    glyphs[i] = new Glyph(0, 0, 0, 0, 0, null);
                    continue;
                }

                using (scope.EnterScope(start))
                    glyphs[i] = ReadGlyph(scope.Reader);
            }

            Glyphs = glyphs;

            for (int i = 0; i < glyphs.Length; i++)
                _ = ResolvePoints(i);

            tables.Add(this);
        });
    }

    public Vector2[] GetPoints(int glyphIndex) => ResolvePoints(glyphIndex);

    public Vector2 GetPoint(int glyphIndex, int pointIndex) => ResolvePoints(glyphIndex)[pointIndex];

    public GlyphOutlineContour[] GetContours(int glyphIndex) => ResolveContours(glyphIndex);

    private static Glyph ReadGlyph(OpenTypeReader reader)
    {
        short numberOfContours = reader.ReadInt16();
        short xMin = reader.ReadInt16();
        short yMin = reader.ReadInt16();
        short xMax = reader.ReadInt16();
        short yMax = reader.ReadInt16();

        GlyphDescription? description = numberOfContours >= 0
            ? ReadSimpleGlyph(reader, numberOfContours)
            : ReadCompositeGlyph(reader);

        return new Glyph(numberOfContours, xMin, yMin, xMax, yMax, description);
    }

    private static SimpleGlyphDescription ReadSimpleGlyph(OpenTypeReader reader, short numberOfContours)
    {
        ushort[] endPtsOfContours = reader.ReadUInt16Array(numberOfContours);
        ushort instructionLength = reader.ReadUInt16();
        byte[] instructions = reader.ReadBytes(instructionLength);

        int numPoints = numberOfContours == 0 ? 0 : endPtsOfContours[^1] + 1;
        var flags = ReadSimpleFlags(reader, numPoints);

        short[] xCoords = new short[numPoints];
        short[] yCoords = new short[numPoints];

        short x = 0;
        for (int i = 0; i < numPoints; i++)
        {
            SimpleGlyphFlags f = flags[i];

            if ((f & SimpleGlyphFlags.X_SHORT_VECTOR) != 0)
            {
                int dx = reader.ReadUInt8();
                x = (short)(x + (((f & SimpleGlyphFlags.X_IS_SAME_OR_POSITIVE_X_SHORT_VECTOR) != 0) ? dx : -dx));
            }
            else if ((f & SimpleGlyphFlags.X_IS_SAME_OR_POSITIVE_X_SHORT_VECTOR) == 0)
            {
                x = (short)(x + reader.ReadInt16());
            }

            xCoords[i] = x;
        }

        short y = 0;
        for (int i = 0; i < numPoints; i++)
        {
            SimpleGlyphFlags f = flags[i];

            if ((f & SimpleGlyphFlags.Y_SHORT_VECTOR) != 0)
            {
                int dy = reader.ReadUInt8();
                y = (short)(y + (((f & SimpleGlyphFlags.Y_IS_SAME_OR_POSITIVE_Y_SHORT_VECTOR) != 0) ? dy : -dy));
            }
            else if ((f & SimpleGlyphFlags.Y_IS_SAME_OR_POSITIVE_Y_SHORT_VECTOR) == 0)
            {
                y = (short)(y + reader.ReadInt16());
            }

            yCoords[i] = y;
        }

        return new SimpleGlyphDescription(endPtsOfContours, instructions, flags, xCoords, yCoords);
    }

    private static SimpleGlyphFlags[] ReadSimpleFlags(OpenTypeReader reader, int pointCount)
    {
        var flags = new SimpleGlyphFlags[pointCount];
        int i = 0;

        while (i < pointCount)
        {
            var raw = (SimpleGlyphFlags)reader.ReadUInt8();
            var flag = raw & ~SimpleGlyphFlags.REPEAT_FLAG;

            flags[i++] = flag;

            if ((raw & SimpleGlyphFlags.REPEAT_FLAG) != 0)
            {
                int repeat = reader.ReadUInt8();
                if (i + repeat > pointCount)
                    throw new InvalidDataException("Repeated simple-glyph flags exceed point count.");

                for (int r = 0; r < repeat; r++)
                    flags[i++] = flag;
            }
        }

        return flags;
    }

    private static CompositeGlyphDescription ReadCompositeGlyph(OpenTypeReader reader)
    {
        var components = new List<CompositeComponent>(4);
        byte[] instructions = [];

        CompositeGlyphFlags flags;
        do
        {
            flags = (CompositeGlyphFlags)reader.ReadUInt16();
            ushort glyphIndex = reader.ReadUInt16();

            int arg1;
            int arg2;

            if ((flags & CompositeGlyphFlags.ARG_1_AND_2_ARE_WORDS) != 0)
            {
                if ((flags & CompositeGlyphFlags.ARGS_ARE_XY_VALUES) != 0)
                {
                    arg1 = reader.ReadInt16();
                    arg2 = reader.ReadInt16();
                }
                else
                {
                    arg1 = reader.ReadUInt16();
                    arg2 = reader.ReadUInt16();
                }
            }
            else
            {
                if ((flags & CompositeGlyphFlags.ARGS_ARE_XY_VALUES) != 0)
                {
                    arg1 = reader.ReadInt8();
                    arg2 = reader.ReadInt8();
                }
                else
                {
                    arg1 = reader.ReadUInt8();
                    arg2 = reader.ReadUInt8();
                }
            }

            Matrix3x2 transform = Matrix3x2.Identity;

            if ((flags & CompositeGlyphFlags.WE_HAVE_A_SCALE) != 0)
            {
                float scale = reader.ReadF2Dot14();
                transform = Matrix3x2.CreateScale(scale);
            }
            else if ((flags & CompositeGlyphFlags.WE_HAVE_AN_X_AND_Y_SCALE) != 0)
            {
                float xScale = reader.ReadF2Dot14();
                float yScale = reader.ReadF2Dot14();
                transform = new Matrix3x2(xScale, 0, 0, yScale, 0, 0);
            }
            else if ((flags & CompositeGlyphFlags.WE_HAVE_A_TWO_BY_TWO) != 0)
            {
                float m00 = reader.ReadF2Dot14();
                float m01 = reader.ReadF2Dot14();
                float m10 = reader.ReadF2Dot14();
                float m11 = reader.ReadF2Dot14();
                transform = new Matrix3x2(m00, m01, m10, m11, 0, 0);
            }

            components.Add(new CompositeComponent(flags, glyphIndex, arg1, arg2, transform));
        }
        while ((flags & CompositeGlyphFlags.MORE_COMPONENTS) != 0);

        if ((flags & CompositeGlyphFlags.WE_HAVE_INSTRUCTIONS) != 0)
        {
            ushort numInstr = reader.ReadUInt16();
            instructions = reader.ReadBytes(numInstr);
        }

        return new CompositeGlyphDescription([.. components], instructions);
    }

    private Vector2[] ResolvePoints(int glyphIndex)
    {
        var pointsCache = resolvedPoints!;
        var state = resolvePointsState!;
        var owner = resolvePointsOwnerThread!;
        int currentThreadId = Environment.CurrentManagedThreadId;

        while (true)
        {
            var cache = pointsCache[glyphIndex];
            if (cache is not null)
                return cache;

            byte currentState = Volatile.Read(ref state[glyphIndex]);
            if (currentState == 2)
                return pointsCache[glyphIndex] ?? [];

            if (currentState == 0)
            {
                if (Interlocked.CompareExchange(ref state[glyphIndex], (byte)1, (byte)0) != 0)
                    continue;

                Volatile.Write(ref owner[glyphIndex], currentThreadId);

                try
                {
                    Glyph glyph = Glyphs[glyphIndex];
                    Vector2[] points;

                    if (glyph.Description is SimpleGlyphDescription simple)
                    {
                        points = new Vector2[simple.XCoordinates.Length];
                        for (int i = 0; i < points.Length; i++)
                            points[i] = new Vector2(simple.XCoordinates[i], simple.YCoordinates[i]);
                    }
                    else if (glyph.Description is CompositeGlyphDescription composite)
                    {
                        points = ResolveCompositePoints(glyphIndex, composite);
                    }
                    else
                    {
                        points = [];
                    }

                    pointsCache[glyphIndex] = points;
                    Volatile.Write(ref state[glyphIndex], (byte)2);
                    return points;
                }
                catch
                {
                    Volatile.Write(ref state[glyphIndex], (byte)0);
                    throw;
                }
                finally
                {
                    Volatile.Write(ref owner[glyphIndex], 0);
                }
            }

            if (Volatile.Read(ref owner[glyphIndex]) == currentThreadId)
                throw new InvalidOperationException("Cyclic composite glyph reference.");

            SpinWait spin = default;
            while (Volatile.Read(ref state[glyphIndex]) == 1)
                spin.SpinOnce();
        }
    }

    private Vector2[] ResolveCompositePoints(int glyphIndex, CompositeGlyphDescription composite)
    {
        int outlinePointCount = 0;
        for (int i = 0; i < composite.Components.Length; i++)
            outlinePointCount += ResolvePoints(composite.Components[i].GlyphIndex).Length;

        Vector2[] parentPhantoms = GetPhantomPoints(glyphIndex);
        var points = new List<Vector2>(outlinePointCount);

        for (int i = 0; i < composite.Components.Length; i++)
        {
            CompositeComponent comp = composite.Components[i];
            Vector2[] childPoints = ResolvePoints(comp.GlyphIndex);
            Vector2[] childAlignmentPoints = ResolveAlignmentPoints(comp.GlyphIndex);

            Vector2 offset = ResolveComponentOffset(comp, points, outlinePointCount, parentPhantoms, childAlignmentPoints);

            for (int p = 0; p < childPoints.Length; p++)
                points.Add(Vector2.Transform(childPoints[p], comp.Transform) + offset);
        }

        return [.. points];
    }

    private Vector2[] ResolveAlignmentPoints(int glyphIndex)
    {
        var alignmentCache = resolvedAlignmentPoints!;
        var state = resolveAlignmentState!;
        var owner = resolveAlignmentOwnerThread!;
        int currentThreadId = Environment.CurrentManagedThreadId;

        while (true)
        {
            var cache = alignmentCache[glyphIndex];
            if (cache is not null)
                return cache;

            byte currentState = Volatile.Read(ref state[glyphIndex]);
            if (currentState == 2)
                return alignmentCache[glyphIndex] ?? [];

            if (currentState == 0)
            {
                if (Interlocked.CompareExchange(ref state[glyphIndex], (byte)1, (byte)0) != 0)
                    continue;

                Volatile.Write(ref owner[glyphIndex], currentThreadId);

                try
                {
                    Vector2[] outlinePoints = ResolvePoints(glyphIndex);
                    Vector2[] phantoms = GetPhantomPoints(glyphIndex);
                    var alignmentPoints = new Vector2[outlinePoints.Length + phantoms.Length];

                    outlinePoints.CopyTo(alignmentPoints, 0);
                    phantoms.CopyTo(alignmentPoints, outlinePoints.Length);

                    alignmentCache[glyphIndex] = alignmentPoints;
                    Volatile.Write(ref state[glyphIndex], (byte)2);
                    return alignmentPoints;
                }
                catch
                {
                    Volatile.Write(ref state[glyphIndex], (byte)0);
                    throw;
                }
                finally
                {
                    Volatile.Write(ref owner[glyphIndex], 0);
                }
            }

            if (Volatile.Read(ref owner[glyphIndex]) == currentThreadId)
                throw new InvalidOperationException("Cyclic composite glyph reference.");

            SpinWait spin = default;
            while (Volatile.Read(ref state[glyphIndex]) == 1)
                spin.SpinOnce();
        }
    }

    private Vector2[] GetPhantomPoints(int glyphIndex)
    {
        Glyph glyph = Glyphs[glyphIndex];

        short lsb = hmtx?.GetLeftSideBearing(glyphIndex) ?? 0;
        ushort advanceWidth = hmtx?.GetAdvanceWidth(glyphIndex) ?? 0;

        float pp0x = glyph.XMin - lsb;
        float pp1x = pp0x + advanceWidth;

        float pp2y;
        float pp3y;

        if (vmtx is { Metrics.Length: > 0 })
        {
            var verticalMetrics = vmtx.GetGlyphMetrics(glyphIndex);
            pp2y = glyph.YMax + verticalMetrics.TopSideBearing;
            pp3y = pp2y - verticalMetrics.AdvanceHeight;
        }
        else
        {
            pp2y = glyph.YMax;
            pp3y = glyph.YMin;
        }

        return
        [
            new Vector2(pp0x, 0),
            new Vector2(pp1x, 0),
            new Vector2(0, pp2y),
            new Vector2(0, pp3y)
        ];
    }

    private GlyphOutlineContour[] ResolveContours(int glyphIndex)
    {
        var contourCache = resolvedContours!;
        var state = resolveContourState!;
        var owner = resolveContourOwnerThread!;
        int currentThreadId = Environment.CurrentManagedThreadId;

        while (true)
        {
            var cache = contourCache[glyphIndex];
            if (cache is not null)
                return cache;

            byte currentState = Volatile.Read(ref state[glyphIndex]);
            if (currentState == 2)
                return contourCache[glyphIndex] ?? [];

            if (currentState == 0)
            {
                if (Interlocked.CompareExchange(ref state[glyphIndex], (byte)1, (byte)0) != 0)
                    continue;

                Volatile.Write(ref owner[glyphIndex], currentThreadId);

                try
                {
                    Glyph glyph = Glyphs[glyphIndex];
                    GlyphOutlineContour[] contours;

                    if (glyph.Description is SimpleGlyphDescription simple)
                    {
                        contours = BuildSimpleContours(simple);
                    }
                    else if (glyph.Description is CompositeGlyphDescription composite)
                    {
                        contours = ResolveCompositeContours(glyphIndex, composite);
                    }
                    else
                    {
                        contours = [];
                    }

                    contourCache[glyphIndex] = contours;
                    Volatile.Write(ref state[glyphIndex], (byte)2);
                    return contours;
                }
                catch
                {
                    Volatile.Write(ref state[glyphIndex], (byte)0);
                    throw;
                }
                finally
                {
                    Volatile.Write(ref owner[glyphIndex], 0);
                }
            }

            if (Volatile.Read(ref owner[glyphIndex]) == currentThreadId)
                throw new InvalidOperationException("Cyclic composite glyph reference.");

            SpinWait spin = default;
            while (Volatile.Read(ref state[glyphIndex]) == 1)
                spin.SpinOnce();
        }
    }

    private static GlyphOutlineContour[] BuildSimpleContours(SimpleGlyphDescription simple)
    {
        if (simple.Flags.Length == 0)
            return [];

        var contours = new GlyphOutlineContour[simple.EndPtsOfContours.Length];
        int start = 0;

        for (int contourIndex = 0; contourIndex < simple.EndPtsOfContours.Length; contourIndex++)
        {
            int end = simple.EndPtsOfContours[contourIndex];
            int length = end - start + 1;

            var points = new GlyphOutlinePoint[length];
            for (int i = 0; i < length; i++)
            {
                int pointIndex = start + i;
                points[i] = new GlyphOutlinePoint(
                    new Vector2(simple.XCoordinates[pointIndex], simple.YCoordinates[pointIndex]),
                    (simple.Flags[pointIndex] & SimpleGlyphFlags.ON_CURVE_POINT) != 0);
            }

            contours[contourIndex] = new GlyphOutlineContour(points);
            start = end + 1;
        }

        return contours;
    }

    private GlyphOutlineContour[] ResolveCompositeContours(int glyphIndex, CompositeGlyphDescription composite)
    {
        int outlinePointCount = 0;
        for (int i = 0; i < composite.Components.Length; i++)
            outlinePointCount += ResolvePoints(composite.Components[i].GlyphIndex).Length;

        Vector2[] parentPhantoms = GetPhantomPoints(glyphIndex);
        var contours = new List<GlyphOutlineContour>(4);
        var parentPoints = new List<Vector2>(32);

        for (int i = 0; i < composite.Components.Length; i++)
        {
            CompositeComponent comp = composite.Components[i];
            GlyphOutlineContour[] childContours = ResolveContours(comp.GlyphIndex);
            Vector2[] childAlignmentPoints = ResolveAlignmentPoints(comp.GlyphIndex);

            Vector2 offset = ResolveComponentOffset(comp, parentPoints, outlinePointCount, parentPhantoms, childAlignmentPoints);

            Vector2[] childPointsFlat = ResolvePoints(comp.GlyphIndex);
            for (int p = 0; p < childPointsFlat.Length; p++)
                parentPoints.Add(Vector2.Transform(childPointsFlat[p], comp.Transform) + offset);

            for (int c = 0; c < childContours.Length; c++)
            {
                var childPoints = childContours[c].Points;
                var points = new GlyphOutlinePoint[childPoints.Length];

                for (int p = 0; p < childPoints.Length; p++)
                {
                    Vector2 position = Vector2.Transform(childPoints[p].Position, comp.Transform) + offset;
                    points[p] = new GlyphOutlinePoint(position, childPoints[p].OnCurve);
                }

                contours.Add(new GlyphOutlineContour(points));
            }
        }

        return [.. contours];
    }

    private static Vector2 ResolveComponentOffset(
        CompositeComponent comp,
        IReadOnlyList<Vector2> parentPoints,
        int outlinePointCount,
        Vector2[] parentPhantoms,
        Vector2[] childAlignmentPoints)
    {
        Vector2 offset;

        if ((comp.Flags & CompositeGlyphFlags.ARGS_ARE_XY_VALUES) != 0)
        {
            offset = new Vector2(comp.Arg1, comp.Arg2);

            bool scaledOffset = (comp.Flags & CompositeGlyphFlags.SCALED_COMPONENT_OFFSET) != 0;
            bool unscaledOffset = (comp.Flags & CompositeGlyphFlags.UNSCALED_COMPONENT_OFFSET) != 0;
            if (scaledOffset && !unscaledOffset)
                offset = Vector2.Transform(offset, comp.Transform);

            if ((comp.Flags & CompositeGlyphFlags.ROUND_XY_TO_GRID) != 0)
                offset = new Vector2(MathF.Round(offset.X), MathF.Round(offset.Y));

            return offset;
        }

        Vector2 parentPoint;
        if ((uint)comp.Arg1 < (uint)parentPoints.Count)
        {
            parentPoint = parentPoints[comp.Arg1];
        }
        else
        {
            int phantomIndex = comp.Arg1 - outlinePointCount;
            if ((uint)phantomIndex >= 4u)
                throw new InvalidOperationException("Composite point reference exceeds parent point count.");

            parentPoint = parentPhantoms[phantomIndex];
        }

        if ((uint)comp.Arg2 >= (uint)childAlignmentPoints.Length)
            throw new InvalidOperationException("Composite point reference exceeds child point count.");

        return parentPoint - Vector2.Transform(childAlignmentPoints[comp.Arg2], comp.Transform);
    }
}
