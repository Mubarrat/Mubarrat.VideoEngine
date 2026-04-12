namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class CbdtTable : IOpenTypeTable
{
    public string Tag => "CBDT";
    public uint Version { get; private set; }
    public byte[] Payload { get; private set; } = [];

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        Version = scope.Reader.ReadVersion16Dot16();
        tables.Add(this);
    }
}
