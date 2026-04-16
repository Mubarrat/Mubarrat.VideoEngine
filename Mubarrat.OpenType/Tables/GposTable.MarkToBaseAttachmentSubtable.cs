using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.TextShaping;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GposTable
{
    public sealed record MarkToBaseAttachmentSubtable(
        ushort Format,
        Coverage MarkCoverage,
        Coverage BaseCoverage,
        ushort MarkClassCount,
        MarkRecord[] MarkArray,
        BaseArrayTable BaseArray) : IGposSubtable
    {
        internal static IGposSubtable Parse(OpenTypeReader.TableScope scope)
        {
            Coverage markCoverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()), baseCoverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            ushort markClassCount = scope.Reader.ReadUInt16();
            return new MarkToBaseAttachmentSubtable(
                1,
                markCoverage,
                baseCoverage,
                markClassCount,
                scope.ParseCommonListTableContiguous<MarkRecord>(scope.Reader.ReadUInt16()),
                scope.ParseCommonTable<BaseArrayTable>(scope.Reader.ReadUInt16(), markClassCount));
        }

        bool IGposSubtable.Apply(GposTable gpos, List<OpenTypeTextShaper.GlyphInfo> buffer, OpenTypeTextShaper.GlyphAdjustment[] adjustments, GdefTable? gdef, int? atIndex, int depth, int recursionLimit)
        {
            bool changed = false;
            int start = atIndex ?? 0;
            int end = atIndex ?? (buffer.Count - 1);

            for (int i = start; i <= end && i < buffer.Count; i++)
            {
                if (!MarkCoverage.TryGetIndex(buffer[i].GlyphId, out ushort markIndex) || markIndex >= MarkArray.Length)
                    continue;

                if (!OpenTypeTextShaper.TryFindPreviousBaseGlyph(buffer, i, BaseCoverage, gdef, out int basePos, out ushort baseCoverageIndex))
                    continue;

                var markRecord = MarkArray[markIndex];
                if (markRecord.MarkClass >= MarkClassCount)
                    continue;

                if (baseCoverageIndex >= BaseArray.BaseRecords.Length)
                    continue;

                var baseRecord = BaseArray.BaseRecords[baseCoverageIndex];
                if (markRecord.MarkClass >= baseRecord.Anchors.Length)
                    continue;

                if (!OpenTypeTextShaper.TryGetAnchorCoordinates(markRecord.MarkAnchor, out short markX, out short markY))
                    continue;
                if (!OpenTypeTextShaper.TryGetAnchorCoordinates(baseRecord.Anchors[markRecord.MarkClass], out short baseX, out short baseY))
                    continue;

                double deltaX = (adjustments[basePos].XOffset + baseX) - (adjustments[i].XOffset + markX);
                double deltaY = (adjustments[basePos].YOffset + baseY) - (adjustments[i].YOffset + markY);

                adjustments[i].XOffset += deltaX;
                adjustments[i].YOffset += deltaY;
                changed = true;
            }

            return changed;
        }
    }
}
