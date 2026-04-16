using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.TextShaping;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GsubTable
{
    public sealed record ReverseChainSingleSubstitutionSubtable(
        ushort Format,
        Coverage Coverage,
        Coverage[] BacktrackCoverages,
        Coverage[] LookaheadCoverages,
        ushort[] SubstituteGlyphIds) : IGsubSubtable
    {
        public bool TrySubstitute(IReadOnlyList<ushort> glyphIds, int index, out ushort substitutedGlyphId)
        {
            substitutedGlyphId = 0;

            if ((uint)index >= (uint)glyphIds.Count)
                return false;

            if (!Coverage.TryGetIndex(glyphIds[index], out ushort coverageIndex))
                return false;
            if (coverageIndex >= SubstituteGlyphIds.Length)
                return false;

            if (index < BacktrackCoverages.Length)
                return false;
            if (index + LookaheadCoverages.Length >= glyphIds.Count)
                return false;

            for (int i = 0; i < BacktrackCoverages.Length; i++)
            {
                if (!BacktrackCoverages[i].TryGetIndex(glyphIds[index - i - 1], out _))
                    return false;
            }

            for (int i = 0; i < LookaheadCoverages.Length; i++)
            {
                if (!LookaheadCoverages[i].TryGetIndex(glyphIds[index + i + 1], out _))
                    return false;
            }

            substitutedGlyphId = SubstituteGlyphIds[coverageIndex];
            return true;
        }

        internal static ReverseChainSingleSubstitutionSubtable Parse(OpenTypeReader.TableScope scope) => new(1,
            scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()),
            Coverage.ParseListFromOffsets16(scope),
            Coverage.ParseListFromOffsets16(scope),
            scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()));

        bool IGsubSubtable.Apply(GsubTable gsub, List<OpenTypeTextShaper.GlyphInfo> buffer, int? atIndex, int depth, int recursionLimit)
        {
            bool changed = false;
            int start = atIndex ?? 0;
            int end = atIndex ?? (buffer.Count - 1);

            for (int i = start; i <= end && i < buffer.Count; i++)
            {
                var glyphIds = OpenTypeTextShaper.ToGlyphIds(buffer);
                if (!TrySubstitute(glyphIds, i, out ushort substituted))
                    continue;
                if (substituted == buffer[i].GlyphId)
                    continue;

                buffer[i] = buffer[i] with { GlyphId = substituted };
                changed = true;
            }

            return changed;
        }
    }
}
