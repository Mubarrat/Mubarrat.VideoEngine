using Mubarrat.OpenType.CommonTables;
using static Mubarrat.OpenType.TextShaping.OpenTypeTextShaper;

namespace Mubarrat.OpenType.Tables;

public sealed partial class GposTable
{
    public interface IGposSubtable : IOpenTypeCommonTable<IGposSubtable>
    {
        static IGposSubtable IOpenTypeCommonTable<IGposSubtable>.Parse(OpenTypeReader.TableScope scope, object? param) => Parse(scope, param);

        public static new IGposSubtable Parse(OpenTypeReader.TableScope scope, object? param)
        {
            if (param is not ushort lookupType)
                return null!;

            ushort format = scope.Reader.ReadUInt16();

            return ((LookupType)lookupType, format) switch
            {
                (LookupType.SingleAdjustment, 1) => SingleAdjustmentSubtable.ParseFormat1(scope),
                (LookupType.SingleAdjustment, 2) => SingleAdjustmentSubtable.ParseFormat2(scope),

                (LookupType.PairAdjustment, 1) => PairAdjustmentSubtable.ParseFormat1(scope),
                (LookupType.PairAdjustment, 2) => PairAdjustmentSubtable.ParseFormat2(scope),

                (LookupType.CursiveAttachment, 1) => CursiveAttachmentSubtable.Parse(scope),

                (LookupType.MarkToBaseAttachment, 1) => MarkToBaseAttachmentSubtable.Parse(scope),
                (LookupType.MarkToLigatureAttachment, 1) => MarkToLigatureAttachmentSubtable.Parse(scope),
                (LookupType.MarkToMarkAttachment, 1) => MarkToMarkAttachmentSubtable.Parse(scope),

                (LookupType.ContextualPositioning, _) => SequenceContext.Parse(scope, format),
                (LookupType.ChainedContextualPositioning, _) => ChainedSequenceContext.Parse(scope, format),

                (LookupType.PositioningExtension, 1) => ExtensionSubstitutionSubtable<IGposSubtable>.Parse(scope),

                _ => throw new InvalidDataException($"Unsupported GPOS lookup type {lookupType} format {format}.")
            };
        }

        internal bool Apply(
            GposTable gpos,
            List<GlyphInfo> buffer,
            GlyphAdjustment[] adjustments,
            GdefTable? gdef,
            int? atIndex,
            int depth,
            int recursionLimit);
    }
}
