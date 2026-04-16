using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.TextShaping;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GposTable
{
    public sealed record SingleAdjustmentSubtable(
        ushort Format,
        Coverage Coverage,
        ValueFormat ValueFormat,
        ValueRecord[] ValueRecords) : IGposSubtable
    {
        public bool TryGetValueRecord(ushort glyphId, out ValueRecord valueRecord)
        {
            valueRecord = default;

            if (!Coverage.TryGetIndex(glyphId, out ushort coverageIndex))
                return false;

            valueRecord = Format == 1
                ? ValueRecords[0]
                : coverageIndex < ValueRecords.Length
                    ? ValueRecords[coverageIndex]
                    : default;

            return true;
        }

        internal static IGposSubtable ParseFormat1(OpenTypeReader.TableScope scope)
        {
            var coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            ValueFormat valueFormat = (ValueFormat)scope.Reader.ReadUInt16();
            return new SingleAdjustmentSubtable(1, coverage, valueFormat, [ValueRecord.Read(scope, valueFormat)]);
        }

        internal static IGposSubtable ParseFormat2(OpenTypeReader.TableScope scope)
        {
            var coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            ValueFormat valueFormat = (ValueFormat)scope.Reader.ReadUInt16();
            var values = new ValueRecord[scope.Reader.ReadUInt16()];
            for (int i = 0; i < values.Length; i++)
                values[i] = ValueRecord.Read(scope, valueFormat);
            return new SingleAdjustmentSubtable(2, coverage, valueFormat, values);
        }

        bool IGposSubtable.Apply(GposTable gpos, List<OpenTypeTextShaper.GlyphInfo> buffer, OpenTypeTextShaper.GlyphAdjustment[] adjustments, GdefTable? gdef, int? atIndex, int depth, int recursionLimit)
        {
            bool changed = false;
            int start = atIndex ?? 0;
            int end = atIndex ?? (buffer.Count - 1);

            for (int i = start; i <= end && i < buffer.Count; i++)
            {
                if (!TryGetValueRecord(buffer[i].GlyphId, out var value))
                    continue;

                changed |= adjustments[i].ApplyValue(value);
            }

            return changed;
        }
    }
}
