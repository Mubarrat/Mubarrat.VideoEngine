using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed class HmtxTable : IOpenTypeTable
{
    public string Tag => "hmtx";

    public LongHorMetric[] Metrics { get; private set; } = [];
    public short[] LeftSideBearings { get; private set; } = [];

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scoped) => tables.Request<HheaTable, MaxpTable>((hhea, maxp, scope) =>
    {
        ushort numberOfHMetrics = hhea.NumberOfHMetrics;
        Metrics = LongHorMetric.ParseListContiguous(scope, numberOfHMetrics);
        LeftSideBearings = maxp.NumGlyphs - numberOfHMetrics is int remaining and > 0 ? scope.Reader.ReadInt16Array(remaining) : [];
        tables.Add(this);
    });

    public readonly record struct LongHorMetric(ushort AdvanceWidth, short LSB) : IOpenTypeCommonTable<LongHorMetric>
    {
        public static LongHorMetric Parse(OpenTypeReader.TableScope scope, object? param = null) => new(scope.Reader.ReadUInt16(), scope.Reader.ReadInt16());
    }

    public ushort GetAdvanceWidth(int glyphId) => (uint)glyphId < (uint)Metrics.Length ? Metrics[glyphId].AdvanceWidth : Metrics[^1].AdvanceWidth;

    public short GetLeftSideBearing(int glyphId)
    {
        if ((uint)glyphId < (uint)Metrics.Length)
            return Metrics[glyphId].LSB;
        int index = glyphId - Metrics.Length;
        if ((uint)index < (uint)LeftSideBearings.Length)
            return LeftSideBearings[index];
        return 0;
    }
}
