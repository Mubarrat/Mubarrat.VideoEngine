namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class JstfTable : IOpenTypeTable
{
    public string Tag => "JSTF";

    public ushort MajorVersion { get; private set; }
    public ushort MinorVersion { get; private set; }
    public JustificationScriptRecord[] Scripts { get; private set; } = [];

    public readonly record struct JustificationScriptRecord(string Tag, ushort Offset);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        MajorVersion = scope.Reader.ReadUInt16();
        MinorVersion = scope.Reader.ReadUInt16();
        ushort scriptCount = scope.Reader.ReadUInt16();

        Scripts = new JustificationScriptRecord[scriptCount];
        for (int i = 0; i < Scripts.Length; i++)
            Scripts[i] = new JustificationScriptRecord(scope.Reader.ReadTag(), scope.Reader.ReadOffset16());

        tables.Add(this);
    }
}
