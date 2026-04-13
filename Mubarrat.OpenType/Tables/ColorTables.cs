using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed class ColrTable : IOpenTypeTable
{
    public string Tag => "COLR";

    public ushort Version { get; private set; }
    public ushort NumBaseGlyphRecords { get; private set; }
    public uint BaseGlyphRecordsOffset { get; private set; }
    public uint LayerRecordsOffset { get; private set; }
    public ushort NumLayerRecords { get; private set; }
    public ColrBaseGlyphRecordV0[] BaseGlyphRecords { get; private set; } = [];
    public ColrLayerRecordV0[] LayerRecords { get; private set; } = [];

    public uint? BaseGlyphListOffset { get; private set; }
    public uint? LayerListOffset { get; private set; }
    public uint? ClipListOffset { get; private set; }
    public uint? VarIndexMapOffset { get; private set; }
    public uint? ItemVariationStoreOffset { get; private set; }

    public ColrBaseGlyphPaintRecordV1[] BaseGlyphPaintRecords { get; private set; } = [];
    public uint[] LayerPaintOffsets { get; private set; } = [];

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Version = scope.Reader.ReadUInt16();
        NumBaseGlyphRecords = scope.Reader.ReadUInt16();
        BaseGlyphRecordsOffset = scope.Reader.ReadUInt32();
        LayerRecordsOffset = scope.Reader.ReadUInt32();
        NumLayerRecords = scope.Reader.ReadUInt16();

        if (NumBaseGlyphRecords > 0 && BaseGlyphRecordsOffset > 0)
        {
            using var baseGlyphScope = scope.EnterScope(BaseGlyphRecordsOffset);
            BaseGlyphRecords = new ColrBaseGlyphRecordV0[NumBaseGlyphRecords];
            for (int i = 0; i < BaseGlyphRecords.Length; i++)
                BaseGlyphRecords[i] = new ColrBaseGlyphRecordV0(
                    baseGlyphScope.Reader.ReadUInt16(),
                    baseGlyphScope.Reader.ReadUInt16(),
                    baseGlyphScope.Reader.ReadUInt16());
        }

        if (NumLayerRecords > 0 && LayerRecordsOffset > 0)
        {
            using var layerScope = scope.EnterScope(LayerRecordsOffset);
            LayerRecords = new ColrLayerRecordV0[NumLayerRecords];
            for (int i = 0; i < LayerRecords.Length; i++)
                LayerRecords[i] = new ColrLayerRecordV0(layerScope.Reader.ReadUInt16(), layerScope.Reader.ReadUInt16());
        }

        if (Version >= 1)
        {
            BaseGlyphListOffset = scope.Reader.ReadUInt32();
            LayerListOffset = scope.Reader.ReadUInt32();
            ClipListOffset = scope.Reader.ReadUInt32();
            VarIndexMapOffset = scope.Reader.ReadUInt32();
            ItemVariationStoreOffset = scope.Reader.ReadUInt32();

            if (BaseGlyphListOffset is > 0)
            {
                using var baseGlyphListScope = scope.EnterScope(BaseGlyphListOffset.Value);
                uint count = baseGlyphListScope.Reader.ReadUInt32();
                BaseGlyphPaintRecords = new ColrBaseGlyphPaintRecordV1[checked((int)count)];
                for (int i = 0; i < BaseGlyphPaintRecords.Length; i++)
                    BaseGlyphPaintRecords[i] = new ColrBaseGlyphPaintRecordV1(
                        baseGlyphListScope.Reader.ReadUInt16(),
                        baseGlyphListScope.Reader.ReadUInt32());
            }

            if (LayerListOffset is > 0)
            {
                using var layerListScope = scope.EnterScope(LayerListOffset.Value);
                uint count = layerListScope.Reader.ReadUInt32();
                LayerPaintOffsets = layerListScope.Reader.ReadUInt32Array(checked((int)count));
            }
        }

        tables.Add(this);
    }
}

public sealed class CpalTable : IOpenTypeTable
{
    public string Tag => "CPAL";

    public ushort Version { get; private set; }
    public ushort NumPaletteEntries { get; private set; }
    public ushort NumPalettes { get; private set; }
    public ushort NumColorRecords { get; private set; }
    public uint OffsetFirstColorRecord { get; private set; }
    public ushort[] ColorRecordIndices { get; private set; } = [];
    public CpalColorRecord[] ColorRecords { get; private set; } = [];
    public uint[] PaletteTypes { get; private set; } = [];
    public ushort[] PaletteLabels { get; private set; } = [];
    public ushort[] PaletteEntryLabels { get; private set; } = [];

    public uint? OffsetPaletteTypeArray { get; private set; }
    public uint? OffsetPaletteLabelArray { get; private set; }
    public uint? OffsetPaletteEntryLabelArray { get; private set; }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Version = scope.Reader.ReadUInt16();
        NumPaletteEntries = scope.Reader.ReadUInt16();
        NumPalettes = scope.Reader.ReadUInt16();
        NumColorRecords = scope.Reader.ReadUInt16();
        OffsetFirstColorRecord = scope.Reader.ReadUInt32();
        ColorRecordIndices = scope.Reader.ReadUInt16Array(NumPalettes);

        if (Version >= 1)
        {
            OffsetPaletteTypeArray = scope.Reader.ReadUInt32();
            OffsetPaletteLabelArray = scope.Reader.ReadUInt32();
            OffsetPaletteEntryLabelArray = scope.Reader.ReadUInt32();

            if (OffsetPaletteTypeArray is > 0)
            {
                using var paletteTypeScope = scope.EnterScope(OffsetPaletteTypeArray.Value);
                PaletteTypes = paletteTypeScope.Reader.ReadUInt32Array(NumPalettes);
            }

            if (OffsetPaletteLabelArray is > 0)
            {
                using var paletteLabelScope = scope.EnterScope(OffsetPaletteLabelArray.Value);
                PaletteLabels = paletteLabelScope.Reader.ReadUInt16Array(NumPalettes);
            }

            if (OffsetPaletteEntryLabelArray is > 0)
            {
                using var paletteEntryLabelScope = scope.EnterScope(OffsetPaletteEntryLabelArray.Value);
                PaletteEntryLabels = paletteEntryLabelScope.Reader.ReadUInt16Array(NumPaletteEntries);
            }
        }

        if (OffsetFirstColorRecord > 0 && NumColorRecords > 0)
        {
            using var colorRecordScope = scope.EnterScope(OffsetFirstColorRecord);
            ColorRecords = new CpalColorRecord[NumColorRecords];
            for (int i = 0; i < ColorRecords.Length; i++)
                ColorRecords[i] = CpalColorRecord.Read(colorRecordScope.Reader);
        }

        tables.Add(this);
    }
}

public sealed class SvgTable : IOpenTypeTable
{
    public string Tag => "SVG ";

    public ushort Version { get; private set; }
    public uint SvgDocumentListOffset { get; private set; }
    public uint Reserved { get; private set; }

    public SvgDocumentRecord[] Documents { get; private set; } = [];

    public readonly record struct SvgDocumentRecord(
        ushort StartGlyphId,
        ushort EndGlyphId,
        uint SvgDocOffset,
        uint SvgDocLength,
        byte[] DocumentData);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Version = scope.Reader.ReadUInt16();
        SvgDocumentListOffset = scope.Reader.ReadUInt32();
        Reserved = scope.Reader.ReadUInt32();

        using var listScope = scope.EnterScope(SvgDocumentListOffset);
        ushort numEntries = listScope.Reader.ReadUInt16();
        Documents = new SvgDocumentRecord[numEntries];

        for (int i = 0; i < Documents.Length; i++)
        {
            ushort startGlyphId = listScope.Reader.ReadUInt16();
            ushort endGlyphId = listScope.Reader.ReadUInt16();
            uint svgDocOffset = listScope.Reader.ReadUInt32();
            uint svgDocLength = listScope.Reader.ReadUInt32();

            using var documentScope = listScope.EnterScope(svgDocOffset);
            byte[] documentData = documentScope.Reader.ReadBytes(checked((int)svgDocLength));

            Documents[i] = new SvgDocumentRecord(startGlyphId, endGlyphId, svgDocOffset, svgDocLength, documentData);
        }

        tables.Add(this);
    }
}
