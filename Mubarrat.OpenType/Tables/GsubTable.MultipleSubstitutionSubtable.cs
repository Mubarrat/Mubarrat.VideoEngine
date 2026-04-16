using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.TextShaping;
using static Mubarrat.OpenType.TextShaping.OpenTypeTextShaper;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GsubTable
{
    public sealed record MultipleSubstitutionSubtable(
        ushort Format,
        Coverage Coverage,
        MultipleSequence[] Sequences) : IGsubSubtable
    {
        public bool TryGetReplacementSequence(ushort glyphId, out ushort[] substituteGlyphIds)
        {
            substituteGlyphIds = [];

            if (!Coverage.TryGetIndex(glyphId, out ushort coverageIndex))
                return false;
            if (coverageIndex >= Sequences.Length)
                return false;

            substituteGlyphIds = Sequences[coverageIndex].SubstituteGlyphIds;
            return true;
        }

        internal static MultipleSubstitutionSubtable Parse(OpenTypeReader.TableScope scope) => new(1,
            Coverage: scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()),
            Sequences: MultipleSequence.ParseListFromOffsets16(scope));

        bool IGsubSubtable.Apply(GsubTable gsub, List<GlyphInfo> buffer, int? atIndex, int depth, int recursionLimit)
        {
            bool changed = false;
            int start = atIndex ?? 0;
            int end = atIndex ?? (buffer.Count - 1);

            for (int i = start; i <= end && i < buffer.Count; i++)
            {
                if (!TryGetReplacementSequence(buffer[i].GlyphId, out var sequence))
                    continue;

                if (sequence.Length == 0)
                {
                    buffer.RemoveAt(i);
                    changed = true;
                    if (atIndex is not null)
                        return true;
                    i--;
                    end--;
                    continue;
                }

                int cluster = buffer[i].Cluster;
                uint codePoint = buffer[i].CodePoint;
                buffer[i] = new GlyphInfo(sequence[0], codePoint, cluster);

                for (int j = 1; j < sequence.Length; j++)
                    buffer.Insert(i + j, new GlyphInfo(sequence[j], codePoint, cluster));

                changed = true;
                if (atIndex is not null)
                    return true;

                int delta = sequence.Length - 1;
                i += delta;
                end += delta;
            }

            return changed;
        }
    }
}
