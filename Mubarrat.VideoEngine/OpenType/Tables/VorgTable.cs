namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class VorgTable : IOpenTypeTable
{
    public string Tag => "VORG";

    public ushort MajorVersion { get; private set; }
    public ushort MinorVersion { get; private set; }
    public short DefaultVertOriginY { get; private set; }
    public VerticalOriginMetric[] VertOriginYMetrics { get; private set; } = [];

    public readonly record struct VerticalOriginMetric(ushort GlyphIndex, short VertOriginY);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        MajorVersion = scope.Reader.ReadUInt16();
        MinorVersion = scope.Reader.ReadUInt16();
        DefaultVertOriginY = scope.Reader.ReadInt16();
        ushort count = scope.Reader.ReadUInt16();

        VertOriginYMetrics = new VerticalOriginMetric[count];
        for (int i = 0; i < VertOriginYMetrics.Length; i++)
            VertOriginYMetrics[i] = new VerticalOriginMetric(scope.Reader.ReadUInt16(), scope.Reader.ReadInt16());

        tables.Add(this);
    }
}
