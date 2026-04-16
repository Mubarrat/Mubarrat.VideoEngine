using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.TextShaping;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GposTable
{
    public sealed record MarkToLigatureAttachmentSubtable(
        ushort Format,
        Coverage MarkCoverage,
        Coverage LigatureCoverage,
        ushort MarkClassCount,
        MarkRecord[] MarkArray,
        LigatureArrayTable LigatureArray) : IGposSubtable
    {
        internal static IGposSubtable Parse(OpenTypeReader.TableScope scope)
        {
            Coverage markCoverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()), ligatureCoverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            ushort markClassCount = scope.Reader.ReadUInt16();
            return new MarkToLigatureAttachmentSubtable(
                1,
                markCoverage,
                ligatureCoverage,
                markClassCount,
                scope.ParseCommonListTableContiguous<MarkRecord>(scope.Reader.ReadUInt16()),
                scope.ParseCommonTable<LigatureArrayTable>(scope.Reader.ReadUInt16(), markClassCount));
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

                if (!OpenTypeTextShaper.TryFindPreviousLigatureGlyph(buffer, i, LigatureCoverage, gdef, out int ligPos, out ushort ligCoverageIndex))
                    continue;

                var markRecord = MarkArray[markIndex];
                if (markRecord.MarkClass >= MarkClassCount)
                    continue;

                if (ligCoverageIndex >= LigatureArray.Ligatures.Length)
                    continue;

                var ligAttach = LigatureArray.Ligatures[ligCoverageIndex];
                if (ligAttach.Components.Length == 0)
                    continue;

                int componentIndex = SelectComponentIndex(buffer, ligPos, i, ligAttach.Components.Length);
                var component = ligAttach.Components[componentIndex];
                if (markRecord.MarkClass >= component.Anchors.Length)
                    continue;

                if (!OpenTypeTextShaper.TryGetAnchorCoordinates(markRecord.MarkAnchor, out short markX, out short markY))
                    continue;
                if (!OpenTypeTextShaper.TryGetAnchorCoordinates(component.Anchors[markRecord.MarkClass], out short ligX, out short ligY))
                    continue;

                double deltaX = (adjustments[ligPos].XOffset + ligX) - (adjustments[i].XOffset + markX);
                double deltaY = (adjustments[ligPos].YOffset + ligY) - (adjustments[i].YOffset + markY);

                adjustments[i].XOffset += deltaX;
                adjustments[i].YOffset += deltaY;
                changed = true;
            }

            return changed;
        }

        private static int SelectComponentIndex(List<OpenTypeTextShaper.GlyphInfo> buffer, int ligaturePos, int markPos, int componentCount)
        {
            if (componentCount <= 1)
                return 0;

            int ligatureCluster = buffer[ligaturePos].Cluster;
            int markCluster = buffer[markPos].Cluster;
            int componentIndex = markCluster - ligatureCluster;

            return Math.Clamp(componentIndex, 0, componentCount - 1);
        }
    }
}
