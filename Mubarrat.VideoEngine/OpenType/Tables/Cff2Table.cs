namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class Cff2Table : IOpenTypeTable
{
    public string Tag => "CFF2";

    public byte Major { get; private set; }
    public byte Minor { get; private set; }
    public byte HeaderSize { get; private set; }
    public ushort TopDictLength { get; private set; }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Major = scope.Reader.ReadUInt8();
        Minor = scope.Reader.ReadUInt8();
        HeaderSize = scope.Reader.ReadUInt8();
        TopDictLength = scope.Reader.ReadUInt16();
        tables.Add(this);
    }
}
