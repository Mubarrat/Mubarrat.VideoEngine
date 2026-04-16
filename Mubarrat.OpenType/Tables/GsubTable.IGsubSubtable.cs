using Mubarrat.OpenType.CommonTables;
using static Mubarrat.OpenType.TextShaping.OpenTypeTextShaper;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GsubTable
{
    public interface IGsubSubtable : IOpenTypeCommonTable<IGsubSubtable>
    {
        static IGsubSubtable IOpenTypeCommonTable<IGsubSubtable>.Parse(OpenTypeReader.TableScope scope, object? param) => Parse(scope, param);

        public static new IGsubSubtable Parse(OpenTypeReader.TableScope scope, object? param)
        {
            if (param is not ushort lookupType)
                return null!;

            ushort format = scope.Reader.ReadUInt16();

            return ((LookupType)lookupType, format) switch
            {
                (LookupType.SingleSubstitution, 1) => SingleSubstitutionSubtable.ParseFormat1(scope),
                (LookupType.SingleSubstitution, 2) => SingleSubstitutionSubtable.ParseFormat2(scope),

                (LookupType.MultipleSubstitution, 1) => MultipleSubstitutionSubtable.Parse(scope),
                (LookupType.AlternateSubstitution, 1) => AlternateSubstitutionSubtable.Parse(scope),
                (LookupType.LigatureSubstitution, 1) => LigatureSubstitutionSubtable.Parse(scope),
                (LookupType.SequenceContext, _) => SequenceContext.Parse(scope, format),
                (LookupType.ChainedSequenceContext, _) => ChainedSequenceContext.Parse(scope, format),
                (LookupType.ExtensionSubstitution, 1) => ExtensionSubstitutionSubtable<IGsubSubtable>.Parse(scope),
                (LookupType.ReverseChainSingleSubstitution, 1) => ReverseChainSingleSubstitutionSubtable.Parse(scope),

                _ => throw new InvalidDataException($"Unsupported GSUB lookup type {lookupType} format {format}.")
            };
        }

        internal bool Apply(
            GsubTable gsub,
            List<GlyphInfo> buffer,
            int? atIndex,
            int depth,
            int recursionLimit);
    }
}
