using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.TextShaping;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GposTable
{
    public sealed record CursiveAttachmentSubtable(
        ushort Format,
        Coverage Coverage,
        EntryExitRecord[] EntryExitRecords) : IGposSubtable
    {
        internal static IGposSubtable Parse(OpenTypeReader.TableScope scope)
        {
            var coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            var records = new EntryExitRecord[scope.Reader.ReadUInt16()];
            for (int i = 0; i < records.Length; i++)
                records[i] = new EntryExitRecord(scope.ParseCommonTableOrDefault<AnchorTable>(scope.Reader.ReadUInt16()), scope.ParseCommonTableOrDefault<AnchorTable>(scope.Reader.ReadUInt16()));
            return new CursiveAttachmentSubtable(1, coverage, records);
        }

        bool IGposSubtable.Apply(GposTable gpos, List<OpenTypeTextShaper.GlyphInfo> buffer, OpenTypeTextShaper.GlyphAdjustment[] adjustments, GdefTable? gdef, int? atIndex, int depth, int recursionLimit)
        {
            if (buffer.Count < 2)
                return false;

            bool changed = false;
            int start = atIndex is null ? 0 : Math.Max(0, atIndex.Value - 1);
            int end = atIndex is null ? buffer.Count - 2 : Math.Min(buffer.Count - 2, atIndex.Value);

            for (int i = start; i <= end; i++)
            {
                if (!Coverage.TryGetIndex(buffer[i].GlyphId, out ushort firstIndex) || firstIndex >= EntryExitRecords.Length)
                    continue;
                if (!Coverage.TryGetIndex(buffer[i + 1].GlyphId, out ushort secondIndex) || secondIndex >= EntryExitRecords.Length)
                    continue;

                var firstRecord = EntryExitRecords[firstIndex];
                var secondRecord = EntryExitRecords[secondIndex];

                if (!OpenTypeTextShaper.TryGetAnchorCoordinates(firstRecord.ExitAnchor, out short exitX, out short exitY))
                    continue;
                if (!OpenTypeTextShaper.TryGetAnchorCoordinates(secondRecord.EntryAnchor, out short entryX, out short entryY))
                    continue;

                double deltaX = (adjustments[i].XOffset + exitX) - (adjustments[i + 1].XOffset + entryX);
                double deltaY = (adjustments[i].YOffset + exitY) - (adjustments[i + 1].YOffset + entryY);

                adjustments[i + 1].XOffset += deltaX;
                adjustments[i + 1].YOffset += deltaY;
                changed = true;
            }

            return changed;
        }
    }
}
