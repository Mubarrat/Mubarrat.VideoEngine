using System.Text;

namespace Mubarrat.OpenType.Tables;

public sealed class Os2Table : IOpenTypeTable
{
    static Os2Table() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public string Tag => "OS/2";

    public ushort Version { get; private set; }
    public short XAvgCharWidth { get; private set; }
    public WeightClass UsWeightClass { get; private set; }
    public WidthClass UsWidthClass { get; private set; }
    public EmbeddingLicensingFlags FsType { get; private set; }

    public short YSubscriptXSize { get; private set; }
    public short YSubscriptYSize { get; private set; }
    public short YSubscriptXOffset { get; private set; }
    public short YSubscriptYOffset { get; private set; }

    public short YSuperscriptXSize { get; private set; }
    public short YSuperscriptYSize { get; private set; }
    public short YSuperscriptXOffset { get; private set; }
    public short YSuperscriptYOffset { get; private set; }

    public short YStrikeoutSize { get; private set; }
    public short YStrikeoutPosition { get; private set; }

    public short SFamilyClass { get; private set; }
    public PanoseData Panose { get; private set; }

    public UnicodeRangeFields UnicodeRange { get; private set; }
    public string AchVendID { get; private set; } = string.Empty;
    public SelectionFlags FsSelection { get; private set; }

    public ushort UsFirstCharIndex { get; private set; }
    public ushort UsLastCharIndex { get; private set; }

    public short STypoAscender { get; private set; }
    public short STypoDescender { get; private set; }
    public short STypoLineGap { get; private set; }
    public ushort UsWinAscent { get; private set; }
    public ushort UsWinDescent { get; private set; }

    public CodePageRangeFields CodePageRange { get; private set; }

    public short SxHeight { get; private set; }
    public short SCapHeight { get; private set; }
    public ushort UsDefaultChar { get; private set; }
    public ushort UsBreakChar { get; private set; }
    public ushort UsMaxContext { get; private set; }

    public ushort UsLowerOpticalPointSize { get; private set; }
    public ushort UsUpperOpticalPointSize { get; private set; }

    public FamilyClassInfo FamilyClass => new(
        (FamilyClassId)((ushort)SFamilyClass >> 8),
        (byte)((ushort)SFamilyClass & 0xFF));

    public readonly record struct FamilyClassInfo(FamilyClassId ClassId, byte SubclassId);

    public enum FamilyClassId : byte
    {
        NoClassification = 0,
        OldstyleSerifs = 1,
        TransitionalSerifs = 2,
        ModernSerifs = 3,
        ClarendonSerifs = 4,
        SlabSerifs = 5,
        Reserved6 = 6,
        FreeformSerifs = 7,
        SansSerif = 8,
        Ornamentals = 9,
        Scripts = 10,
        Reserved11 = 11,
        Symbolic = 12,
        Reserved13 = 13,
        Reserved14 = 14
    }

    public enum WeightClass : ushort
    {
        Thin = 100,
        ExtraLight = 200,
        UltraLight = 200,
        Light = 300,
        Normal = 400,
        Regular = 400,
        Medium = 500,
        SemiBold = 600,
        DemiBold = 600,
        Bold = 700,
        ExtraBold = 800,
        UltraBold = 800,
        Black = 900,
        Heavy = 900
    }

    public enum WidthClass : ushort
    {
        UltraCondensed = 1,
        ExtraCondensed = 2,
        Condensed = 3,
        SemiCondensed = 4,
        Medium = 5,
        Normal = 5,
        SemiExpanded = 6,
        Expanded = 7,
        ExtraExpanded = 8,
        UltraExpanded = 9
    }

    [Flags]
    public enum EmbeddingLicensingFlags : ushort
    {
        InstallableEmbedding = 0x0000,
        RestrictedLicenseEmbedding = 0x0002,
        PreviewAndPrintEmbedding = 0x0004,
        EditableEmbedding = 0x0008,
        NoSubsetting = 0x0100,
        BitmapEmbeddingOnly = 0x0200
    }

    [Flags]
    public enum SelectionFlags : ushort
    {
        Italic = 1 << 0,
        Underscore = 1 << 1,
        Negative = 1 << 2,
        Outlined = 1 << 3,
        Strikeout = 1 << 4,
        Bold = 1 << 5,
        Regular = 1 << 6,
        UseTypoMetrics = 1 << 7,
        Wws = 1 << 8,
        Oblique = 1 << 9
    }

    public readonly record struct PanoseData(
        byte FamilyKind,
        byte SerifStyle,
        byte Weight,
        byte Proportion,
        byte Contrast,
        byte StrokeVariation,
        byte ArmStyle,
        byte Letterform,
        byte Midline,
        byte XHeight);

    public readonly record struct UnicodeRangeFields(
        uint Range1,
        uint Range2,
        uint Range3,
        uint Range4)
    {
        public bool HasBit(int bitIndex)
        {
            if ((uint)bitIndex >= 128)
                return false;

            int word = bitIndex >> 5;
            int bit = bitIndex & 31;

            return word switch
            {
                0 => (Range1 & (1u << bit)) != 0,
                1 => (Range2 & (1u << bit)) != 0,
                2 => (Range3 & (1u << bit)) != 0,
                3 => (Range4 & (1u << bit)) != 0,
                _ => false
            };
        }
    }

    public readonly record struct CodePageRangeFields(
        uint Range1,
        uint Range2)
    {
        public bool HasBit(int bitIndex)
        {
            if ((uint)bitIndex >= 64)
                return false;

            if (bitIndex < 32)
                return (Range1 & (1u << bitIndex)) != 0;

            bitIndex -= 32;
            return (Range2 & (1u << bitIndex)) != 0;
        }
    }

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scoped)
    {
        var reader = scoped.Reader;

        Version = reader.ReadUInt16();
        XAvgCharWidth = reader.ReadInt16();
        UsWeightClass = (WeightClass)reader.ReadUInt16();
        UsWidthClass = (WidthClass)reader.ReadUInt16();
        FsType = (EmbeddingLicensingFlags)reader.ReadUInt16();

        YSubscriptXSize = reader.ReadInt16();
        YSubscriptYSize = reader.ReadInt16();
        YSubscriptXOffset = reader.ReadInt16();
        YSubscriptYOffset = reader.ReadInt16();

        YSuperscriptXSize = reader.ReadInt16();
        YSuperscriptYSize = reader.ReadInt16();
        YSuperscriptXOffset = reader.ReadInt16();
        YSuperscriptYOffset = reader.ReadInt16();

        YStrikeoutSize = reader.ReadInt16();
        YStrikeoutPosition = reader.ReadInt16();

        SFamilyClass = reader.ReadInt16();

        Panose = new PanoseData(
            reader.ReadUInt8(),
            reader.ReadUInt8(),
            reader.ReadUInt8(),
            reader.ReadUInt8(),
            reader.ReadUInt8(),
            reader.ReadUInt8(),
            reader.ReadUInt8(),
            reader.ReadUInt8(),
            reader.ReadUInt8(),
            reader.ReadUInt8());

        UnicodeRange = new UnicodeRangeFields(
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32(),
            reader.ReadUInt32());

        AchVendID = reader.ReadTag();
        FsSelection = (SelectionFlags)reader.ReadUInt16();
        UsFirstCharIndex = reader.ReadUInt16();
        UsLastCharIndex = reader.ReadUInt16();

        if (Version >= 1)
        {
            STypoAscender = reader.ReadInt16();
            STypoDescender = reader.ReadInt16();
            STypoLineGap = reader.ReadInt16();
            UsWinAscent = reader.ReadUInt16();
            UsWinDescent = reader.ReadUInt16();

            CodePageRange = new CodePageRangeFields(
                reader.ReadUInt32(),
                reader.ReadUInt32());
        }

        if (Version >= 2)
        {
            SxHeight = reader.ReadInt16();
            SCapHeight = reader.ReadInt16();
            UsDefaultChar = reader.ReadUInt16();
            UsBreakChar = reader.ReadUInt16();
            UsMaxContext = reader.ReadUInt16();
        }

        if (Version >= 5)
        {
            UsLowerOpticalPointSize = reader.ReadUInt16();
            UsUpperOpticalPointSize = reader.ReadUInt16();
        }

        tables.Add(this);
    }
}
