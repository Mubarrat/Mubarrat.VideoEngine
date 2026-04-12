using Mubarrat.VideoEngine.OpenType.CommonTables;

namespace Mubarrat.VideoEngine.OpenType.Tables;

public sealed class GsubTable : IOpenTypeTable
{
    public const string TableTag = "GSUB";

    public string Tag => TableTag;

    public GsubHeader Header { get; private set; }
    public ScriptRecord[] ScriptList { get; private set; } = [];
    public FeatureRecord[] FeatureList { get; private set; } = [];
    public Lookup<IGsubSubtable>[] LookupList { get; private set; } = [];
    public FeatureVariations? FeatureVariations { get; private set; }

    public readonly record struct GsubHeader(ushort MajorVersion, ushort MinorVersion);

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
                (LookupType.ExtensionSubstitution, 1) => ExtensionSubstitutionSubtable.Parse(scope),
                (LookupType.ReverseChainSingleSubstitution, 1) => ReverseChainSingleSubstitutionSubtable.Parse(scope),

                _ => throw new InvalidDataException($"Unsupported GSUB lookup type {lookupType} format {format}.")
            };
        }
    }

    public sealed record SingleSubstitutionSubtable(
        ushort Format,
        Coverage Coverage,
        short? DeltaGlyphId,
        ushort[]? SubstituteGlyphIds) : IGsubSubtable
    {
        internal static IGsubSubtable ParseFormat1(OpenTypeReader.TableScope scope) => new SingleSubstitutionSubtable(1, scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()), scope.Reader.ReadInt16(), null);

        internal static IGsubSubtable ParseFormat2(OpenTypeReader.TableScope scope) => new SingleSubstitutionSubtable(2, scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16()), null, scope.Reader.ReadUInt16Array(scope.Reader.ReadUInt16()));
    }

    public sealed record MultipleSubstitutionSubtable(
        ushort Format,
        Coverage Coverage,
        MultipleSequence[] Sequences) : IGsubSubtable
    {
        internal static IGsubSubtable Parse(OpenTypeReader.TableScope scope)
        {
            var coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            var sequences = new MultipleSequence[scope.Reader.ReadUInt16()];
            for (int i = 0; i < sequences.Length; i++)
                using (var sequenceScope = scope.EnterScope(scope.Reader.ReadUInt16()))
                    sequences[i] = new MultipleSequence(sequenceScope.Reader.ReadUInt16Array(sequenceScope.Reader.ReadUInt16()));
            return new MultipleSubstitutionSubtable(1, coverage, sequences);
        }
    }

    public readonly record struct MultipleSequence(ushort[] SubstituteGlyphIds);

    public sealed record AlternateSubstitutionSubtable(
        ushort Format,
        Coverage Coverage,
        AlternateSet[] AlternateSets) : IGsubSubtable
    {
        internal static IGsubSubtable Parse(OpenTypeReader.TableScope scope)
        {
            var coverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadUInt16());
            var sets = new AlternateSet[scope.Reader.ReadUInt16()];
            for (int i = 0; i < sets.Length; i++)
                using (var setScope = scope.EnterScope(scope.Reader.ReadUInt16()))
                    sets[i] = new AlternateSet(setScope.Reader.ReadUInt16Array(setScope.Reader.ReadUInt16()));
            return new AlternateSubstitutionSubtable(1, coverage, sets);
        }
    }

    public readonly record struct AlternateSet(ushort[] AlternateGlyphIds);

    public sealed record LigatureSubstitutionSubtable(
        ushort Format,
        Coverage Coverage,
        LigatureSet[] LigatureSets) : IGsubSubtable
    {
        internal static IGsubSubtable Parse(OpenTypeReader.TableScope scope)
        {
            ushort coverageOffset = scope.Reader.ReadUInt16();
            ushort setCount = scope.Reader.ReadUInt16();

            var setOffsets = new ushort[setCount];
            for (int i = 0; i < setCount; i++)
                setOffsets[i] = scope.Reader.ReadUInt16();

            using (var coverageScope = scope.EnterScope(coverageOffset))
            {
                var coverage = Coverage.Parse(coverageScope);
                var sets = new LigatureSet[setCount];

                for (int i = 0; i < setCount; i++)
                {
                    using (var setScope = scope.EnterScope(setOffsets[i]))
                    {
                        ushort ligatureCount = setScope.Reader.ReadUInt16();
                        var ligatureOffsets = new ushort[ligatureCount];

                        for (int j = 0; j < ligatureCount; j++)
                            ligatureOffsets[j] = setScope.Reader.ReadUInt16();

                        var ligatures = new Ligature[ligatureCount];

                        for (int j = 0; j < ligatureCount; j++)
                        {
                            using (var ligatureScope = setScope.EnterScope(ligatureOffsets[j]))
                            {
                                ushort ligatureGlyph = ligatureScope.Reader.ReadUInt16();
                                ushort componentCount = ligatureScope.Reader.ReadUInt16();

                                var componentGlyphIds = new ushort[Math.Max(componentCount - 1, 0)];
                                for (int c = 0; c < componentGlyphIds.Length; c++)
                                    componentGlyphIds[c] = ligatureScope.Reader.ReadUInt16();

                                ligatures[j] = new Ligature(ligatureGlyph, componentGlyphIds);
                            }
                        }

                        sets[i] = new LigatureSet(ligatures);
                    }
                }

                return new LigatureSubstitutionSubtable(1, coverage, sets);
            }
        }
    }

    public readonly record struct LigatureSet(Ligature[] Ligatures);
    public readonly record struct Ligature(ushort LigatureGlyph, ushort[] ComponentGlyphIds);

    public sealed record ExtensionSubstitutionSubtable(
        ushort Format,
        ushort ExtensionLookupType,
        IGsubSubtable ExtensionSubtable) : IGsubSubtable
    {
        internal static IGsubSubtable Parse(OpenTypeReader.TableScope scope)
        {
            ushort extensionLookupType = scope.Reader.ReadUInt16();
            return new ExtensionSubstitutionSubtable(1, extensionLookupType, scope.ParseCommonTable<IGsubSubtable>(scope.Reader.ReadUInt32(), extensionLookupType));
        }
    }

    public sealed record ReverseChainSingleSubstitutionSubtable(
        ushort Format,
        Coverage Coverage,
        Coverage[] BacktrackCoverages,
        Coverage[] LookaheadCoverages,
        ushort[] SubstituteGlyphIds) : IGsubSubtable
    {
        internal static IGsubSubtable Parse(OpenTypeReader.TableScope scope)
        {
            ushort coverageOffset = scope.Reader.ReadUInt16();
            ushort backtrackCount = scope.Reader.ReadUInt16();

            var backtrackOffsets = new ushort[backtrackCount];
            for (int i = 0; i < backtrackCount; i++)
                backtrackOffsets[i] = scope.Reader.ReadUInt16();

            ushort lookaheadCount = scope.Reader.ReadUInt16();
            var lookaheadOffsets = new ushort[lookaheadCount];
            for (int i = 0; i < lookaheadCount; i++)
                lookaheadOffsets[i] = scope.Reader.ReadUInt16();

            ushort substituteCount = scope.Reader.ReadUInt16();
            var substituteGlyphIds = new ushort[substituteCount];
            for (int i = 0; i < substituteCount; i++)
                substituteGlyphIds[i] = scope.Reader.ReadUInt16();

            using (var coverageScope = scope.EnterScope(coverageOffset))
            {
                var coverage = Coverage.Parse(coverageScope);

                var backtrackCoverages = new Coverage[backtrackCount];
                for (int i = 0; i < backtrackCount; i++)
                {
                    using (var backtrackScope = scope.EnterScope(backtrackOffsets[i]))
                        backtrackCoverages[i] = Coverage.Parse(backtrackScope);
                }

                var lookaheadCoverages = new Coverage[lookaheadCount];
                for (int i = 0; i < lookaheadCount; i++)
                {
                    using (var lookaheadScope = scope.EnterScope(lookaheadOffsets[i]))
                        lookaheadCoverages[i] = Coverage.Parse(lookaheadScope);
                }

                return new ReverseChainSingleSubstitutionSubtable(1, coverage, backtrackCoverages, lookaheadCoverages, substituteGlyphIds);
            }
        }
    }

    public enum LookupType : ushort
    {
        SingleSubstitution = 1,
        MultipleSubstitution = 2,
        AlternateSubstitution = 3,
        LigatureSubstitution = 4,
        SequenceContext = 5,
        ChainedSequenceContext = 6,
        ExtensionSubstitution = 7,
        ReverseChainSingleSubstitution = 8
    }

    internal static double ReadF2Dot14(short value) => value / 16384.0;

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        ushort major = scope.Reader.ReadUInt16(), minor = scope.Reader.ReadUInt16();
        Header = new GsubHeader(major, minor);
        ScriptList = scope.ParseCommonListTableContiguous<ScriptRecord>(scope.Reader.ReadUInt16());
        FeatureList = scope.ParseCommonListTableContiguous<FeatureRecord>(scope.Reader.ReadUInt16());
        LookupList = scope.ParseCommonListTableFromOffsets16<Lookup<IGsubSubtable>>(scope.Reader.ReadUInt16());
        if (major >= 1 && minor >= 1)
            FeatureVariations = scope.ParseCommonTable<FeatureVariations>(scope.Reader.ReadUInt32());
        tables.Add(this);
    }
}
