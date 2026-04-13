using System.Numerics;

namespace Mubarrat.OpenType.Tables;

public sealed class GlyfTable : IOpenTypeTable
{
    public string Tag => "glyf";

    public Glyph[] Glyphs { get; private set; } = [];

    private Vector2[][]? resolvedPoints;
    private byte[]? resolveState;

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

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope) => tables.Request<MaxpTable, LocaTable>((maxp, loca, scope) =>
    {
        var glyphs = new Glyph[maxp.NumGlyphs];
        resolvedPoints = new Vector2[maxp.NumGlyphs][];
        resolveState = new byte[maxp.NumGlyphs];

        // Parse each glyph using loca offsets
        for (int i = 0; i < maxp.NumGlyphs; i++)
        {
            int start = (int)loca.Offsets[i];
            if (start == (int)loca.Offsets[i + 1])
            {
                glyphs[i] = new Glyph(0, 0, 0, 0, 0, null); // Empty glyph
                continue;
            }
            using (scope.EnterScope(start))
                glyphs[i] = ReadGlyph(scope.Reader);
        }

        Glyphs = glyphs;

        // Precompute points for all glyphs
        for (int i = 0; i < glyphs.Length; i++)
            _ = ResolvePoints(i);

        tables.Add(this);
    });

    public Vector2[] GetPoints(int glyphIndex)
        => ResolvePoints(glyphIndex);

    public Vector2 GetPoint(int glyphIndex, int pointIndex)
        => ResolvePoints(glyphIndex)[pointIndex];

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
        short y = 0;

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
                transform *= reader.ReadF2Dot14();
            }
            else if ((flags & CompositeGlyphFlags.WE_HAVE_AN_X_AND_Y_SCALE) != 0)
            {
                transform = new(reader.ReadF2Dot14(), 0, 0, reader.ReadF2Dot14(), 0, 0);
            }
            else if ((flags & CompositeGlyphFlags.WE_HAVE_A_TWO_BY_TWO) != 0)
            {
                transform = new(
                    reader.ReadF2Dot14(),
                    reader.ReadF2Dot14(),
                    reader.ReadF2Dot14(),
                    reader.ReadF2Dot14(),
                    0, 0);
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
        var cache = resolvedPoints![glyphIndex];
        if (cache is not null)
            return cache;

        if (resolveState![glyphIndex] == 1)
            throw new InvalidOperationException("Cyclic composite glyph reference.");

        if (resolveState[glyphIndex] == 2)
            return resolvedPoints[glyphIndex] ?? [];

        resolveState[glyphIndex] = 1;

        Glyph glyph = Glyphs[glyphIndex];
        Vector2[] points;

        if (glyph.Description is SimpleGlyphDescription sgd)
        {
            points = new Vector2[sgd.XCoordinates.Length];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector2(sgd.XCoordinates[i], sgd.YCoordinates[i]);
        }
        else if (glyph.Description is CompositeGlyphDescription cgd)
        {
            points = ResolveCompositePoints(cgd);
        }
        else
        {
            points = [];
        }

        resolvedPoints[glyphIndex] = points;
        resolveState[glyphIndex] = 2;
        return points;
    }

    private Vector2[] ResolveCompositePoints(CompositeGlyphDescription composite)
    {
        int capacity = 0;
        for (int i = 0; i < composite.Components.Length; i++)
            capacity += ResolvePoints(composite.Components[i].GlyphIndex).Length;

        var points = new List<Vector2>(capacity);

        for (int i = 0; i < composite.Components.Length; i++)
        {
            CompositeComponent comp = composite.Components[i];
            Vector2[] childPoints = ResolvePoints(comp.GlyphIndex);

            Vector2 offset;

            if ((comp.Flags & CompositeGlyphFlags.ARGS_ARE_XY_VALUES) != 0)
            {
                offset = new Vector2(comp.Arg1, comp.Arg2);

                if ((comp.Flags & CompositeGlyphFlags.SCALED_COMPONENT_OFFSET) != 0 &&
                    (comp.Flags & CompositeGlyphFlags.UNSCALED_COMPONENT_OFFSET) == 0)
                {
                    offset = Vector2.Transform(offset, comp.Transform);
                }

                if ((comp.Flags & CompositeGlyphFlags.ROUND_XY_TO_GRID) != 0)
                {
                    offset = Vector2.Round(offset);
                }
            }
            else
            {
                if ((uint)comp.Arg1 >= (uint)points.Count)
                    throw new InvalidOperationException("Composite point reference exceeds parent point count.");

                if ((uint)comp.Arg2 >= (uint)childPoints.Length)
                    throw new InvalidOperationException("Composite point reference exceeds child point count.");

                Vector2 parentPoint = points[comp.Arg1];
                Vector2 childPoint = Vector2.Transform(childPoints[comp.Arg2], comp.Transform);
                offset = parentPoint - childPoint;
            }

            for (int p = 0; p < childPoints.Length; p++)
                points.Add(Vector2.Transform(childPoints[p], comp.Transform) + offset);
        }

        return [.. points];
    }
}
