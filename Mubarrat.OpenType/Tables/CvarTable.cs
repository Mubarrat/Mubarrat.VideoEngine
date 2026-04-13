namespace Mubarrat.OpenType.Tables;

public sealed class CvarTable : IOpenTypeTable
{
    public string Tag => "cvar";

    public ushort MajorVersion { get; private set; }
    public ushort MinorVersion { get; private set; }
    public ushort TupleVariationCount { get; private set; }
    public ushort DataOffset { get; private set; }

    public ushort TupleCount { get; private set; }
    public ushort TupleFlags { get; private set; }
    public TupleVariationHeader[] TupleVariationHeaders { get; private set; } = [];

    public readonly record struct TupleVariationHeader(
        ushort VariationDataSize,
        ushort TupleIndex,
        float[]? PeakTuple,
        float[]? IntermediateStartTuple,
        float[]? IntermediateEndTuple);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scoped) => tables.Request<FvarTable>((fvar, scope) =>
    {
        const ushort EmbeddedPeakTupleFlag = 0x8000;
        const ushort IntermediateRegionFlag = 0x4000;

        MajorVersion = scope.Reader.ReadUInt16();
        MinorVersion = scope.Reader.ReadUInt16();
        scope.Reader.ReadUInt16(); // reserved
        TupleVariationCount = scope.Reader.ReadUInt16();
        DataOffset = scope.Reader.ReadUInt16();

        TupleCount = (ushort)(TupleVariationCount & 0x0FFF);
        TupleFlags = (ushort)(TupleVariationCount & 0xF000);

        int axisCount = fvar.Axes.Length;
        TupleVariationHeaders = new TupleVariationHeader[TupleCount];
        for (int i = 0; i < TupleVariationHeaders.Length; i++)
        {
            ushort variationDataSize = scope.Reader.ReadUInt16();
            ushort tupleIndex = scope.Reader.ReadUInt16();

            float[]? peakTuple = null;
            float[]? intermediateStartTuple = null;
            float[]? intermediateEndTuple = null;

            if ((tupleIndex & EmbeddedPeakTupleFlag) != 0)
            {
                peakTuple = new float[axisCount];
                for (int axis = 0; axis < axisCount; axis++)
                    peakTuple[axis] = scope.Reader.ReadF2Dot14();
            }

            if ((tupleIndex & IntermediateRegionFlag) != 0)
            {
                intermediateStartTuple = new float[axisCount];
                intermediateEndTuple = new float[axisCount];

                for (int axis = 0; axis < axisCount; axis++)
                    intermediateStartTuple[axis] = scope.Reader.ReadF2Dot14();
                for (int axis = 0; axis < axisCount; axis++)
                    intermediateEndTuple[axis] = scope.Reader.ReadF2Dot14();
            }

            TupleVariationHeaders[i] = new TupleVariationHeader(
                variationDataSize,
                tupleIndex,
                peakTuple,
                intermediateStartTuple,
                intermediateEndTuple);
        }

        tables.Add(this);
    });
}

public sealed class CvtTable : IOpenTypeTable
{
    public string Tag => "cvt ";

    public short[] Values { get; private set; } = [];

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        tables.Request<HeadTable>((head, tableScope) =>
        {
            int count = head.UnitsPerEm;
            Values = count > 0 ? tableScope.Reader.ReadInt16Array(count) : [];
            tables.Add(this);
        });
    }
}

public sealed class FpgmTable : IOpenTypeTable
{
    public string Tag => "fpgm";

    public byte[] Instructions { get; private set; } = [];

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Instructions = [];
        tables.Add(this);
    }
}

public sealed class PrepTable : IOpenTypeTable
{
    public string Tag => "prep";

    public byte[] Instructions { get; private set; } = [];

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Instructions = [];
        tables.Add(this);
    }
}

public sealed class GvarTable : IOpenTypeTable
{
    public string Tag => "gvar";

    public ushort MajorVersion { get; private set; }
    public ushort MinorVersion { get; private set; }
    public ushort AxisCount { get; private set; }
    public ushort SharedTupleCount { get; private set; }
    public uint SharedTuplesOffset { get; private set; }
    public ushort GlyphCount { get; private set; }
    public ushort Flags { get; private set; }
    public uint GlyphVariationDataArrayOffset { get; private set; }
    public uint[] GlyphVariationDataOffsets { get; private set; } = [];

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        MajorVersion = scope.Reader.ReadUInt16();
        MinorVersion = scope.Reader.ReadUInt16();
        AxisCount = scope.Reader.ReadUInt16();
        SharedTupleCount = scope.Reader.ReadUInt16();
        SharedTuplesOffset = scope.Reader.ReadUInt32();
        GlyphCount = scope.Reader.ReadUInt16();
        Flags = scope.Reader.ReadUInt16();
        GlyphVariationDataArrayOffset = scope.Reader.ReadUInt32();

        bool longOffsets = (Flags & 0x0001) != 0;
        int count = GlyphCount + 1;
        GlyphVariationDataOffsets = new uint[count];
        if (longOffsets)
        {
            for (int i = 0; i < count; i++)
                GlyphVariationDataOffsets[i] = scope.Reader.ReadUInt32();
        }
        else
        {
            for (int i = 0; i < count; i++)
                GlyphVariationDataOffsets[i] = (uint)scope.Reader.ReadUInt16() * 2u;
        }

        tables.Add(this);
    }
}
