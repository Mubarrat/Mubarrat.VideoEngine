namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class LtshTable : IOpenTypeTable
{
    public string Tag => "LTSH";

    public ushort Version { get; private set; }
    public ushort NumGlyphs { get; private set; }
    public byte[] YPels { get; private set; } = [];

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scoped) => tables.Request<MaxpTable>((maxp, scope) =>
    {
        Version = scope.Reader.ReadUInt16();
        NumGlyphs = scope.Reader.ReadUInt16();
        int glyphCount = NumGlyphs != 0 ? NumGlyphs : maxp.NumGlyphs;
        YPels = scope.Reader.ReadBytes(glyphCount);
        tables.Add(this);
    });
}
