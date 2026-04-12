namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class LocaTable : IOpenTypeTable
{
    public uint[] Offsets { get; private set; }

    public string Tag => "loca";

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope) => tables.Request<HeadTable, MaxpTable>((head, maxp, scope) =>
    {
        Offsets = head.IndexToLocFormat is HeadTable.IndexToLocFormatEnum.Short
            ? Array.ConvertAll(scope.Reader.ReadUInt16Array(maxp.NumGlyphs + 1), x => x * 2u)
            : scope.Reader.ReadUInt32Array(maxp.NumGlyphs + 1);
        tables.Add(this);
    });
}
