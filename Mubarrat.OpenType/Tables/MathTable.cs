using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed class MathTable : IOpenTypeTable
{
    public string Tag => "MATH";

    public ushort MajorVersion { get; set; }
    public ushort MinorVersion { get; set; }

    public MathConstantsData MathConstants { get; set; }
    public MathGlyphInfoData MathGlyphInfo { get; set; }
    public MathVariantsData MathVariants { get; set; }

    public readonly record struct MathValueRecord(short Value, DeviceOrVariationIndexTable? DeviceTable) : IOpenTypeCommonTable<MathValueRecord>
    {
        public static MathValueRecord Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
            Value: scope.Reader.ReadInt16(),
            DeviceTable: scope.ParseCommonTableOrDefault<DeviceOrVariationIndexTable>(scope.Reader.ReadOffset16()));

        public static implicit operator MathValueRecord(short value) => new(value, null);
        public static double operator *(MathValueRecord record, double scale) => record.Value * scale;
    }

    public readonly record struct MathConstantsData(
        short ScriptPercentScaleDown,
        short ScriptScriptPercentScaleDown,
        ushort DelimitedSubFormulaMinHeight,
        ushort DisplayOperatorMinHeight,
        MathValueRecord MathLeading,
        MathValueRecord AxisHeight,
        MathValueRecord AccentBaseHeight,
        MathValueRecord FlattenedAccentBaseHeight,
        MathValueRecord SubscriptShiftDown,
        MathValueRecord SubscriptTopMax,
        MathValueRecord SubscriptBaselineDropMin,
        MathValueRecord SuperscriptShiftUp,
        MathValueRecord SuperscriptShiftUpCramped,
        MathValueRecord SuperscriptBottomMin,
        MathValueRecord SuperscriptBaselineDropMax,
        MathValueRecord SubSuperscriptGapMin,
        MathValueRecord SuperscriptBottomMaxWithSubscript,
        MathValueRecord SpaceAfterScript,
        MathValueRecord UpperLimitGapMin,
        MathValueRecord UpperLimitBaselineRiseMin,
        MathValueRecord LowerLimitGapMin,
        MathValueRecord LowerLimitBaselineDropMin,
        MathValueRecord StackTopShiftUp,
        MathValueRecord StackTopDisplayStyleShiftUp,
        MathValueRecord StackBottomShiftDown,
        MathValueRecord StackBottomDisplayStyleShiftDown,
        MathValueRecord StackGapMin,
        MathValueRecord StackDisplayStyleGapMin,
        MathValueRecord StretchStackTopShiftUp,
        MathValueRecord StretchStackBottomShiftDown,
        MathValueRecord StretchStackGapAboveMin,
        MathValueRecord StretchStackGapBelowMin,
        MathValueRecord FractionNumeratorShiftUp,
        MathValueRecord FractionNumeratorDisplayStyleShiftUp,
        MathValueRecord FractionDenominatorShiftDown,
        MathValueRecord FractionDenominatorDisplayStyleShiftDown,
        MathValueRecord FractionNumeratorGapMin,
        MathValueRecord FractionNumeratorDisplayStyleGapMin,
        MathValueRecord FractionRuleThickness,
        MathValueRecord FractionDenominatorGapMin,
        MathValueRecord FractionDenominatorDisplayStyleGapMin,
        MathValueRecord SkewedFractionHorizontalGap,
        MathValueRecord SkewedFractionVerticalGap,
        MathValueRecord OverbarVerticalGap,
        MathValueRecord OverbarRuleThickness,
        MathValueRecord OverbarExtraAscender,
        MathValueRecord UnderbarVerticalGap,
        MathValueRecord UnderbarRuleThickness,
        MathValueRecord UnderbarExtraDescender,
        MathValueRecord RadicalVerticalGap,
        MathValueRecord RadicalDisplayStyleVerticalGap,
        MathValueRecord RadicalRuleThickness,
        MathValueRecord RadicalExtraAscender,
        MathValueRecord RadicalKernBeforeDegree,
        MathValueRecord RadicalKernAfterDegree,
        short RadicalDegreeBottomRaisePercent) : IOpenTypeCommonTable<MathConstantsData>
    {
        public static MathConstantsData Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
            ScriptPercentScaleDown: scope.Reader.ReadInt16(),
            ScriptScriptPercentScaleDown: scope.Reader.ReadInt16(),
            DelimitedSubFormulaMinHeight: scope.Reader.ReadUInt16(),
            DisplayOperatorMinHeight: scope.Reader.ReadUInt16(),
            MathLeading: MathValueRecord.Parse(scope),
            AxisHeight: MathValueRecord.Parse(scope),
            AccentBaseHeight: MathValueRecord.Parse(scope),
            FlattenedAccentBaseHeight: MathValueRecord.Parse(scope),
            SubscriptShiftDown: MathValueRecord.Parse(scope),
            SubscriptTopMax: MathValueRecord.Parse(scope),
            SubscriptBaselineDropMin: MathValueRecord.Parse(scope),
            SuperscriptShiftUp: MathValueRecord.Parse(scope),
            SuperscriptShiftUpCramped: MathValueRecord.Parse(scope),
            SuperscriptBottomMin: MathValueRecord.Parse(scope),
            SuperscriptBaselineDropMax: MathValueRecord.Parse(scope),
            SubSuperscriptGapMin: MathValueRecord.Parse(scope),
            SuperscriptBottomMaxWithSubscript: MathValueRecord.Parse(scope),
            SpaceAfterScript: MathValueRecord.Parse(scope),
            UpperLimitGapMin: MathValueRecord.Parse(scope),
            UpperLimitBaselineRiseMin: MathValueRecord.Parse(scope),
            LowerLimitGapMin: MathValueRecord.Parse(scope),
            LowerLimitBaselineDropMin: MathValueRecord.Parse(scope),
            StackTopShiftUp: MathValueRecord.Parse(scope),
            StackTopDisplayStyleShiftUp: MathValueRecord.Parse(scope),
            StackBottomShiftDown: MathValueRecord.Parse(scope),
            StackBottomDisplayStyleShiftDown: MathValueRecord.Parse(scope),
            StackGapMin: MathValueRecord.Parse(scope),
            StackDisplayStyleGapMin: MathValueRecord.Parse(scope),
            StretchStackTopShiftUp: MathValueRecord.Parse(scope),
            StretchStackBottomShiftDown: MathValueRecord.Parse(scope),
            StretchStackGapAboveMin: MathValueRecord.Parse(scope),
            StretchStackGapBelowMin: MathValueRecord.Parse(scope),
            FractionNumeratorShiftUp: MathValueRecord.Parse(scope),
            FractionNumeratorDisplayStyleShiftUp: MathValueRecord.Parse(scope),
            FractionDenominatorShiftDown: MathValueRecord.Parse(scope),
            FractionDenominatorDisplayStyleShiftDown: MathValueRecord.Parse(scope),
            FractionNumeratorGapMin: MathValueRecord.Parse(scope),
            FractionNumeratorDisplayStyleGapMin: MathValueRecord.Parse(scope),
            FractionRuleThickness: MathValueRecord.Parse(scope),
            FractionDenominatorGapMin: MathValueRecord.Parse(scope),
            FractionDenominatorDisplayStyleGapMin: MathValueRecord.Parse(scope),
            SkewedFractionHorizontalGap: MathValueRecord.Parse(scope),
            SkewedFractionVerticalGap: MathValueRecord.Parse(scope),
            OverbarVerticalGap: MathValueRecord.Parse(scope),
            OverbarRuleThickness: MathValueRecord.Parse(scope),
            OverbarExtraAscender: MathValueRecord.Parse(scope),
            UnderbarVerticalGap: MathValueRecord.Parse(scope),
            UnderbarRuleThickness: MathValueRecord.Parse(scope),
            UnderbarExtraDescender: MathValueRecord.Parse(scope),
            RadicalVerticalGap: MathValueRecord.Parse(scope),
            RadicalDisplayStyleVerticalGap: MathValueRecord.Parse(scope),
            RadicalRuleThickness: MathValueRecord.Parse(scope),
            RadicalExtraAscender: MathValueRecord.Parse(scope),
            RadicalKernBeforeDegree: MathValueRecord.Parse(scope),
            RadicalKernAfterDegree: MathValueRecord.Parse(scope),
            RadicalDegreeBottomRaisePercent: scope.Reader.ReadInt16());
    }

    public readonly record struct MathItalicsCorrectionInfo(Coverage Coverage, MathValueRecord[] ItalicsCorrections) : IOpenTypeCommonTable<MathItalicsCorrectionInfo>
    {
        public static MathItalicsCorrectionInfo Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
            Coverage: scope.ParseCommonTable<Coverage>(scope.Reader.ReadOffset16()),
            ItalicsCorrections: MathValueRecord.ParseListContiguous(scope));
    }

    public readonly record struct MathTopAccentAttachmentInfo(Coverage Coverage, MathValueRecord[] TopAccentAttachments) : IOpenTypeCommonTable<MathTopAccentAttachmentInfo>
    {
        public static MathTopAccentAttachmentInfo Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
            Coverage: scope.ParseCommonTable<Coverage>(scope.Reader.ReadOffset16()),
            TopAccentAttachments: MathValueRecord.ParseListContiguous(scope));
    }

    public readonly record struct MathKernTable(MathValueRecord[] CorrectionHeights, MathValueRecord[] KernValues) : IOpenTypeCommonTable<MathKernTable>
    {
        public static MathKernTable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            ushort heightCount = scope.Reader.ReadUInt16();
            return new(
                CorrectionHeights: MathValueRecord.ParseListContiguous(scope, heightCount),
                KernValues: MathValueRecord.ParseListContiguous(scope, heightCount + 1));
        }
    }

    public readonly record struct MathKernInfoRecord(
        MathKernTable? TopRightMathKern,
        MathKernTable? TopLeftMathKern,
        MathKernTable? BottomRightMathKern,
        MathKernTable? BottomLeftMathKern) : IOpenTypeCommonTable<MathKernInfoRecord>
    {
        public static MathKernInfoRecord Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
            TopRightMathKern: scope.ParseCommonTableOrDefault<MathKernTable>(scope.Reader.ReadOffset16()),
            TopLeftMathKern: scope.ParseCommonTableOrDefault<MathKernTable>(scope.Reader.ReadOffset16()),
            BottomRightMathKern: scope.ParseCommonTableOrDefault<MathKernTable>(scope.Reader.ReadOffset16()),
            BottomLeftMathKern: scope.ParseCommonTableOrDefault<MathKernTable>(scope.Reader.ReadOffset16()));
    }

    public readonly record struct MathKernInfoData(Coverage Coverage, MathKernInfoRecord[] Records) : IOpenTypeCommonTable<MathKernInfoData>
    {
        public static MathKernInfoData Parse(OpenTypeReader.TableScope scope, object? param = null) => new MathKernInfoData(
            Coverage: scope.ParseCommonTable<Coverage>(scope.Reader.ReadOffset16()),
            Records: MathKernInfoRecord.ParseListContiguous(scope));
    }

    public readonly record struct MathGlyphInfoData(
        MathItalicsCorrectionInfo? ItalicsCorrectionInfo,
        MathTopAccentAttachmentInfo? TopAccentAttachment,
        Coverage? ExtendedShapeCoverage,
        MathKernInfoData? MathKernInfo) : IOpenTypeCommonTable<MathGlyphInfoData>
    {
        public static MathGlyphInfoData Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
            ItalicsCorrectionInfo: scope.ParseCommonTableOrDefault<MathItalicsCorrectionInfo>(scope.Reader.ReadOffset16()),
            TopAccentAttachment: scope.ParseCommonTableOrDefault<MathTopAccentAttachmentInfo>(scope.Reader.ReadOffset16()),
            ExtendedShapeCoverage: scope.ParseCommonTableOrDefault<Coverage>(scope.Reader.ReadOffset16()),
            MathKernInfo: scope.ParseCommonTableOrDefault<MathKernInfoData>(scope.Reader.ReadOffset16()));
    }

    public readonly record struct MathGlyphVariantRecord(ushort VariantGlyph, ushort AdvanceMeasurement) : IOpenTypeCommonTable<MathGlyphVariantRecord>
    {
        public static MathGlyphVariantRecord Parse(OpenTypeReader.TableScope scope, object? param = null) => new(scope.Reader.ReadUInt16(), scope.Reader.ReadUInt16());
    }

    [Flags]
    public enum GlyphPartFlags : ushort { Extender = 0x0001 }

    public readonly record struct GlyphPart(
        ushort GlyphId,
        ushort StartConnectorLength,
        ushort EndConnectorLength,
        ushort FullAdvance,
        GlyphPartFlags PartFlags) : IOpenTypeCommonTable<GlyphPart>
    {
        public static GlyphPart Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
            GlyphId: scope.Reader.ReadUInt16(),
            StartConnectorLength: scope.Reader.ReadUInt16(),
            EndConnectorLength: scope.Reader.ReadUInt16(),
            FullAdvance: scope.Reader.ReadUInt16(),
            PartFlags: (GlyphPartFlags)scope.Reader.ReadUInt16());
    }

    public readonly record struct GlyphAssembly(MathValueRecord ItalicsCorrection, GlyphPart[] PartRecords) : IOpenTypeCommonTable<GlyphAssembly>
    {
        public static GlyphAssembly Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
            ItalicsCorrection: MathValueRecord.Parse(scope),
            PartRecords: GlyphPart.ParseListContiguous(scope));
    }

    public readonly record struct MathGlyphConstruction(
        GlyphAssembly? GlyphAssembly,
        MathGlyphVariantRecord[] Variants) : IOpenTypeCommonTable<MathGlyphConstruction>
    {
        public static MathGlyphConstruction Parse(OpenTypeReader.TableScope scope, object? param = null) => new(
            GlyphAssembly: scope.ParseCommonTableOrDefault<GlyphAssembly>(scope.Reader.ReadOffset16()),
            Variants: MathGlyphVariantRecord.ParseListContiguous(scope));
    }

    public readonly record struct MathVariantsData(
        ushort MinConnectorOverlap,
        Coverage VerticalGlyphCoverage,
        Coverage HorizontalGlyphCoverage,
        MathGlyphConstruction[] VerticalConstructions,
        MathGlyphConstruction[] HorizontalConstructions) : IOpenTypeCommonTable<MathVariantsData>
    {
        public static MathVariantsData Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            ushort minConnectorOverlap = scope.Reader.ReadUInt16();
            Coverage vertCoverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadOffset16()), horizCoverage = scope.ParseCommonTable<Coverage>(scope.Reader.ReadOffset16());
            ushort vertGlyphCount = scope.Reader.ReadUInt16(), horizGlyphCount = scope.Reader.ReadUInt16();
            return new MathVariantsData(
                minConnectorOverlap,
                vertCoverage,
                horizCoverage,
                MathGlyphConstruction.ParseListFromOffsets16(scope, vertGlyphCount),
                MathGlyphConstruction.ParseListFromOffsets16(scope, horizGlyphCount));
        }
    }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        MajorVersion = scope.Reader.ReadUInt16();
        MinorVersion = scope.Reader.ReadUInt16();
        MathConstants = scope.ParseCommonTable<MathConstantsData>(scope.Reader.ReadOffset16());
        MathGlyphInfo = scope.ParseCommonTable<MathGlyphInfoData>(scope.Reader.ReadOffset16());
        MathVariants = scope.ParseCommonTable<MathVariantsData>(scope.Reader.ReadOffset16());
        tables.Add(this);
    }
}
