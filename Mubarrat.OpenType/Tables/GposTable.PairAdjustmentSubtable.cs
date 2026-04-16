using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.TextShaping;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GposTable
{
    public sealed record PairAdjustmentSubtable(
        ushort Format,
        Coverage Coverage,
        ValueFormat ValueFormat1,
        ValueFormat ValueFormat2,
        PairSet[]? PairSets,
        ClassDef? ClassDef1,
        ClassDef? ClassDef2,
        ushort Class1Count,
        ushort Class2Count,
        PairClass1Record[]? Class1Records) : IGposSubtable
    {
        public bool TryGetPairAdjustment(ushort firstGlyphId, ushort secondGlyphId, out ValueRecord value1, out ValueRecord value2)
        {
            value1 = default;
            value2 = default;

            if (!Coverage.TryGetIndex(firstGlyphId, out ushort coverageIndex))
                return false;

            if (Format == 1 && PairSets is not null)
            {
                if (coverageIndex >= PairSets.Length)
                    return false;

                var pairs = PairSets[coverageIndex].Pairs;
                for (int i = 0; i < pairs.Length; i++)
                {
                    if (pairs[i].SecondGlyph != secondGlyphId)
                        continue;

                    value1 = pairs[i].Value1;
                    value2 = pairs[i].Value2;
                    return true;
                }

                return false;
            }

            if (Format == 2 && ClassDef1 is ClassDef classDef1 && ClassDef2 is ClassDef classDef2 && Class1Records is not null)
            {
                ushort class1 = classDef1.TryGetClass(firstGlyphId, out var c1) ? c1 : (ushort)0;
                ushort class2 = classDef2.TryGetClass(secondGlyphId, out var c2) ? c2 : (ushort)0;

                if (class1 >= Class1Records.Length)
                    return false;

                var class2Records = Class1Records[class1].Class2Records;
                if (class2 >= class2Records.Length)
                    return false;

                value1 = class2Records[class2].Value1;
                value2 = class2Records[class2].Value2;
                return true;
            }

            return false;
        }

        internal static IGposSubtable ParseFormat1(OpenTypeReader.TableScope scope)
        {
            var coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            ValueFormat valueFormat1 = (ValueFormat)scope.Reader.ReadUInt16(), valueFormat2 = (ValueFormat)scope.Reader.ReadUInt16();
            var pairSets = new PairSet[scope.Reader.ReadUInt16()];
            for (int i = 0; i < pairSets.Length; i++)
            {
                using (var pairSetScope = scope.EnterScope(scope.Reader.ReadOffset16()))
                {
                    var pairs = new PairValueRecord[pairSetScope.Reader.ReadUInt16()];
                    for (int p = 0; p < pairs.Length; p++)
                        pairs[p] = new PairValueRecord(pairSetScope.Reader.ReadUInt16(), ValueRecord.Read(pairSetScope, valueFormat1), ValueRecord.Read(pairSetScope, valueFormat2));
                    pairSets[i] = new PairSet(pairs);
                }
            }
            return new PairAdjustmentSubtable(1, coverage, valueFormat1, valueFormat2, pairSets, null, null, 0, 0, null);
        }

        internal static IGposSubtable ParseFormat2(OpenTypeReader.TableScope scope)
        {
            var coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            ValueFormat valueFormat1 = (ValueFormat)scope.Reader.ReadUInt16(), valueFormat2 = (ValueFormat)scope.Reader.ReadUInt16();
            ClassDef classDef1 = scope.ParseCommonTable<ClassDef>(scope.Reader.ReadUInt16()), classDef2 = scope.ParseCommonTable<ClassDef>(scope.Reader.ReadUInt16());
            ushort class1Count = scope.Reader.ReadUInt16(), class2Count = scope.Reader.ReadUInt16();
            var class1Records = new PairClass1Record[class1Count];
            for (int c1 = 0; c1 < class1Count; c1++)
            {
                var class2Records = new PairClass2Record[class2Count];
                for (int c2 = 0; c2 < class2Count; c2++)
                    class2Records[c2] = new PairClass2Record(ValueRecord.Read(scope, valueFormat1), ValueRecord.Read(scope, valueFormat2));
                class1Records[c1] = new PairClass1Record(class2Records);
            }
            return new PairAdjustmentSubtable(2, coverage, valueFormat1, valueFormat2, null, classDef1, classDef2, class1Count, class2Count, class1Records);
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
                if (!TryGetPairAdjustment(buffer[i].GlyphId, buffer[i + 1].GlyphId, out var value1, out var value2))
                    continue;

                changed |= adjustments[i].ApplyValue(value1);
                changed |= adjustments[i + 1].ApplyValue(value2);
            }

            return changed;
        }
    }
}
