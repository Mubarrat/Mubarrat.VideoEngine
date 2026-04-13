using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed class VmtxTable : IOpenTypeTable
{
    public string Tag => "vmtx";

    public VMetricsEntry[] Metrics { get; private set; } = [];
    public short[] TopSideBearings { get; private set; } = [];

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scoped) => tables.Request<VheaTable, MaxpTable>((vhea, maxp, scope) =>
    {
        var metricCount = vhea.NumOfLongVerMetrics;
        Metrics = VMetricsEntry.ParseListContiguous(scope, metricCount);
        TopSideBearings = maxp.NumGlyphs - metricCount is int remaining and > 0 ? scope.Reader.ReadInt16Array(remaining) : [];
        tables.Add(this);
    });

    public readonly record struct VMetricsEntry(ushort AdvanceHeight, short TopSideBearing) : IOpenTypeCommonTable<VMetricsEntry>
    {
        public static VMetricsEntry Parse(OpenTypeReader.TableScope scope, object? param = null) => new(scope.Reader.ReadUInt16(), scope.Reader.ReadInt16());
    }

    public VMetricsEntry GetGlyphMetrics(int glyphId)
    {
        if (glyphId < Metrics.Length)
            return Metrics[glyphId];

        var last = Metrics[^1];

        var tsbIndex = glyphId - Metrics.Length;
        if (tsbIndex < TopSideBearings.Length)
            return new VMetricsEntry
            {
                AdvanceHeight = last.AdvanceHeight,
                TopSideBearing = TopSideBearings[tsbIndex]
            };

        return last;
    }
}
