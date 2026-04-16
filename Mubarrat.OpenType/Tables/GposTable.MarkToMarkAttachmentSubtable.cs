using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.TextShaping;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GposTable
{
    public sealed record MarkToMarkAttachmentSubtable(
        ushort Format,
        Coverage Mark1Coverage,
        Coverage Mark2Coverage,
        ushort MarkClassCount,
        MarkRecord[] Mark1Array,
        Mark2ArrayTable Mark2Array) : IGposSubtable
    {
        internal static IGposSubtable Parse(OpenTypeReader.TableScope scope)
        {
            Coverage mark1Coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()), mark2Coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            ushort markClassCount = scope.Reader.ReadUInt16();
            return new MarkToMarkAttachmentSubtable(
                1,
                mark1Coverage,
                mark2Coverage,
                markClassCount,
                scope.ParseCommonListTableContiguous<MarkRecord>(scope.Reader.ReadUInt16()),
                scope.ParseCommonTable<Mark2ArrayTable>(scope.Reader.ReadUInt16(), markClassCount));
        }

        bool IGposSubtable.Apply(GposTable gpos, List<OpenTypeTextShaper.GlyphInfo> buffer, OpenTypeTextShaper.GlyphAdjustment[] adjustments, GdefTable? gdef, int? atIndex, int depth, int recursionLimit)
        {
            bool changed = false;
            int start = atIndex ?? 0;
            int end = atIndex ?? (buffer.Count - 1);

            for (int i = start; i <= end && i < buffer.Count; i++)
            {
                if (!Mark1Coverage.TryGetIndex(buffer[i].GlyphId, out ushort mark1Index) || mark1Index >= Mark1Array.Length)
                    continue;

                if (!OpenTypeTextShaper.TryFindPreviousMarkGlyph(buffer, i, Mark2Coverage, gdef, out int mark2Pos, out ushort mark2CoverageIndex))
                    continue;

                var mark1Record = Mark1Array[mark1Index];
                if (mark1Record.MarkClass >= MarkClassCount)
                    continue;

                if (mark2CoverageIndex >= Mark2Array.Mark2Records.Length)
                    continue;

                var mark2Record = Mark2Array.Mark2Records[mark2CoverageIndex];
                if (mark1Record.MarkClass >= mark2Record.Anchors.Length)
                    continue;

                if (!OpenTypeTextShaper.TryGetAnchorCoordinates(mark1Record.MarkAnchor, out short mark1X, out short mark1Y))
                    continue;
                if (!OpenTypeTextShaper.TryGetAnchorCoordinates(mark2Record.Anchors[mark1Record.MarkClass], out short mark2X, out short mark2Y))
                    continue;

                double deltaX = (adjustments[mark2Pos].XOffset + mark2X) - (adjustments[i].XOffset + mark1X);
                double deltaY = (adjustments[mark2Pos].YOffset + mark2Y) - (adjustments[i].YOffset + mark1Y);

                adjustments[i].XOffset += deltaX;
                adjustments[i].YOffset += deltaY;
                changed = true;
            }

            return changed;
        }
    }
}
