namespace Mubarrat.OpenType.Tables;

public sealed class PostTable : IOpenTypeTable
{
    public const string TableTag = "post";

    public string Tag => TableTag;

    public uint Version { get; private set; }
    public float ItalicAngle { get; private set; }
    public short UnderlinePosition { get; private set; }
    public short UnderlineThickness { get; private set; }
    public uint IsFixedPitch { get; private set; }
    public uint MinMemType42 { get; private set; }
    public uint MaxMemType42 { get; private set; }
    public uint MinMemType1 { get; private set; }
    public uint MaxMemType1 { get; private set; }

    public ushort? NumberOfGlyphs { get; private set; }
    public ushort[] GlyphNameIndex { get; private set; } = [];
    public string[] CustomGlyphNames { get; private set; } = [];

    public sbyte[] GlyphNameOffsets { get; private set; } = [];

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Version = scope.Reader.ReadVersion16Dot16();
        ItalicAngle = scope.Reader.ReadFixed();
        UnderlinePosition = scope.Reader.ReadInt16();
        UnderlineThickness = scope.Reader.ReadInt16();
        IsFixedPitch = scope.Reader.ReadUInt32();
        MinMemType42 = scope.Reader.ReadUInt32();
        MaxMemType42 = scope.Reader.ReadUInt32();
        MinMemType1 = scope.Reader.ReadUInt32();
        MaxMemType1 = scope.Reader.ReadUInt32();

        if (Version == 0x00020000)
            ParseVersion2(scope);
        else if (Version == 0x00025000)
            ParseVersion25(scope);
        else if (Version != 0x00010000 && Version != 0x00030000)
            throw new InvalidDataException($"Unsupported post version 0x{Version:X8}.");

        tables.Add(this);
    }

    private void ParseVersion2(OpenTypeReader.TableScope scope)
    {
        ushort numGlyphs = scope.Reader.ReadUInt16();
        NumberOfGlyphs = numGlyphs;

        GlyphNameIndex = scope.Reader.ReadUInt16Array(numGlyphs);

        ushort maxIndex = 0;
        for (int i = 0; i < GlyphNameIndex.Length; i++)
            if (GlyphNameIndex[i] > maxIndex)
                maxIndex = GlyphNameIndex[i];

        int customCount = maxIndex >= 258 ? maxIndex - 257 : 0;
        if (customCount == 0)
        {
            CustomGlyphNames = [];
            return;
        }

        CustomGlyphNames = new string[customCount];
        for (int i = 0; i < CustomGlyphNames.Length; i++)
        {
            byte length = scope.Reader.ReadUInt8();
            CustomGlyphNames[i] = length == 0
                ? string.Empty
                : System.Text.Encoding.ASCII.GetString(scope.Reader.ReadBytes(length));
        }
    }

    private void ParseVersion25(OpenTypeReader.TableScope scope)
    {
        ushort numGlyphs = scope.Reader.ReadUInt16();
        NumberOfGlyphs = numGlyphs;
        GlyphNameOffsets = new sbyte[numGlyphs];
        for (int i = 0; i < GlyphNameOffsets.Length; i++)
            GlyphNameOffsets[i] = scope.Reader.ReadInt8();
    }
}
