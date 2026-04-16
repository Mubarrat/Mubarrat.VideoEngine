using Mubarrat.OpenType.CommonTables;
using Mubarrat.OpenType.TextShaping;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GsubTable
{
    public sealed record SingleSubstitutionSubtable(
        ushort Format,
        Coverage Coverage,
        short? DeltaGlyphId,
        ushort[]? SubstituteGlyphIds) : IGsubSubtable
    {
        public bool TrySubstitute(ushort glyphId, out ushort substitutedGlyphId)
        {
            substitutedGlyphId = glyphId;

            if (!Coverage.TryGetIndex(glyphId, out ushort coverageIndex))
                return false;

            if (Format == 1 && DeltaGlyphId is short delta)
            {
                substitutedGlyphId = unchecked((ushort)(glyphId + delta));
                return true;
            }

            if (Format == 2 && SubstituteGlyphIds is not null && coverageIndex < SubstituteGlyphIds.Length)
            {
                substitutedGlyphId = SubstituteGlyphIds[coverageIndex];
                return true;
            }

            return false;
        }

        internal static SingleSubstitutionSubtable ParseFormat1(OpenTypeReader.TableScope scope) => new(1, scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()), scope.Reader.ReadInt16(), null);

        internal static SingleSubstitutionSubtable ParseFormat2(OpenTypeReader.TableScope scope) => new(2, scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()), null, scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()));

        bool IGsubSubtable.Apply(GsubTable gsub, List<OpenTypeTextShaper.GlyphInfo> buffer, int? atIndex, int depth, int recursionLimit)
        {
            bool changed = false;
            int start = atIndex ?? 0;
            int end = atIndex ?? (buffer.Count - 1);

            for (int i = start; i <= end && i < buffer.Count; i++)
            {
                if (!TrySubstitute(buffer[i].GlyphId, out ushort substituted))
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
