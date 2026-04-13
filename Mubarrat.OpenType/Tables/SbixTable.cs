using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed class SbixTable : IOpenTypeTable
{
    public string Tag => "sbix";

    public ushort MajorVersion { get; private set; }
    public ushort MinorVersion { get; private set; }
    public ushort Flags { get; private set; }
    public uint NumStrikes { get; private set; }
    public uint[] StrikeOffsets { get; private set; } = [];
    public SbixStrikeRecord[] Strikes { get; private set; } = [];

    [Flags]
    public enum SbixFlags : ushort
    {
        DrawOutlines = 0x0002
    }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope) => tables.Request<MaxpTable>((maxp, tableScope) =>
    {
        MajorVersion = tableScope.Reader.ReadUInt16();
        MinorVersion = tableScope.Reader.ReadUInt16();
        Flags = tableScope.Reader.ReadUInt16();
        NumStrikes = tableScope.Reader.ReadUInt32();

        StrikeOffsets = tableScope.Reader.ReadUInt32Array(checked((int)NumStrikes));
        Strikes = new SbixStrikeRecord[StrikeOffsets.Length];

        int glyphCount = maxp.NumGlyphs;
        for (int strikeIndex = 0; strikeIndex < Strikes.Length; strikeIndex++)
        {
            using var strikeScope = tableScope.EnterScope(StrikeOffsets[strikeIndex]);
            ushort ppem = strikeScope.Reader.ReadUInt16();
            ushort ppi = strikeScope.Reader.ReadUInt16();
            uint[] glyphDataOffsets = strikeScope.Reader.ReadUInt32Array(glyphCount + 1);

            SbixGlyphDataRecord[] glyphs = new SbixGlyphDataRecord[glyphCount];
            for (int glyphIndex = 0; glyphIndex < glyphs.Length; glyphIndex++)
            {
                uint startOffset = glyphDataOffsets[glyphIndex];
                uint endOffset = glyphDataOffsets[glyphIndex + 1];
                if (endOffset <= startOffset)
                {
                    glyphs[glyphIndex] = new SbixGlyphDataRecord(0, 0, string.Empty, [], null);
                    continue;
                }

                using var glyphScope = strikeScope.EnterScope(startOffset);
                short originOffsetX = glyphScope.Reader.ReadInt16();
                short originOffsetY = glyphScope.Reader.ReadInt16();
                string graphicType = glyphScope.Reader.ReadTag();

                int payloadLength = checked((int)(endOffset - startOffset - 8));
                byte[] data = payloadLength > 0 ? glyphScope.Reader.ReadBytes(payloadLength) : [];
                ushort? dupeGlyphId = graphicType == "dupe" && data.Length >= 2
                    ? (ushort)((data[0] << 8) | data[1])
                    : null;

                glyphs[glyphIndex] = new SbixGlyphDataRecord(originOffsetX, originOffsetY, graphicType, data, dupeGlyphId);
            }

            Strikes[strikeIndex] = new SbixStrikeRecord(ppem, ppi, glyphDataOffsets, glyphs);
        }

        tables.Add(this);
    });
}
