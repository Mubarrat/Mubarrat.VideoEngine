namespace Mubarrat.OpenType.Tables;

public sealed class HeadTable : IOpenTypeTable
{
    public ushort MajorVersion { get; set; }
    public ushort MinorVersion { get; set; }
    public float FontRevision { get; set; }
    public uint ChecksumAdjustment { get; set; }
    public uint MagicNumber { get; set; }
    public HeadFlags Flags { get; set; }
    public ushort UnitsPerEm { get; set; }
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public short XMin { get; set; }
    public short YMin { get; set; }
    public short XMax { get; set; }
    public short YMax { get; set; }
    public MacStyleFlags MacStyle { get; set; }
    public ushort LowestRecPPEM { get; set; }
    public FontDirectionHintEnum FontDirectionHint { get; set; }
    public IndexToLocFormatEnum IndexToLocFormat { get; set; }
    public GlyphDataFormatEnum GlyphDataFormat { get; set; }

    public string Tag => "head";

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scoped)
    {
        var reader = scoped.Reader;
        MajorVersion = reader.ReadUInt16();
        MinorVersion = reader.ReadUInt16();
        FontRevision = reader.ReadFixed();
        ChecksumAdjustment = reader.ReadUInt32();
        MagicNumber = reader.ReadUInt32();
        Flags = (HeadFlags)reader.ReadUInt16();
        UnitsPerEm = reader.ReadUInt16();
        Created = reader.ReadLongDateTime();
        Modified = reader.ReadLongDateTime();
        XMin = reader.ReadInt16();
        YMin = reader.ReadInt16();
        XMax = reader.ReadInt16();
        YMax = reader.ReadInt16();
        MacStyle = (MacStyleFlags)reader.ReadUInt16();
        LowestRecPPEM = reader.ReadUInt16();
        FontDirectionHint = (FontDirectionHintEnum)reader.ReadInt16();
        IndexToLocFormat = (IndexToLocFormatEnum)reader.ReadInt16();
        GlyphDataFormat = (GlyphDataFormatEnum)reader.ReadInt16();
        tables.Add(this);
    }

    [Flags]
    public enum HeadFlags : ushort
    {
        /// <summary>
        /// Baseline for font at y=0.
        /// </summary>
        BaselineAtY0 = 1 << 0,

        /// <summary>
        /// Left sidebearing point at x=0 (relevant only for TrueType rasterizers) 
        /// — see additional information regarding variable fonts.
        /// </summary>
        LeftSidebearingAtX0 = 1 << 1,

        /// <summary>
        /// Instructions may depend on point size.
        /// </summary>
        InstructionsDependOnPointSize = 1 << 2,

        /// <summary>
        /// Force ppem to integer values for all internal scaler math; may use fractional ppem sizes if this bit is clear. 
        /// It is strongly recommended that this be set in hinted fonts.
        /// </summary>
        ForcePpemToInteger = 1 << 3,

        /// <summary>
        /// Instructions may alter advance width (the advance widths might not scale linearly).
        /// </summary>
        InstructionsMayAlterAdvanceWidth = 1 << 4,

        /// <summary>
        /// This bit is not used in OpenType, and should not be set to ensure compatible behavior on all platforms. 
        /// If set, it may result in different behavior for vertical layout in some platforms.
        /// </summary>
        ReservedBit5 = 1 << 5,

        /// <summary>
        /// These bits are not used in OpenType and should always be cleared. 
        /// (Bits 6-10)
        /// </summary>
        ReservedBits6to10 = 0b11111 << 6, // Bits 6-10

        /// <summary>
        /// Font data is “lossless” as a result of being subjected to optimizing transformation and/or compression, 
        /// where original functionality and features are retained but binary compatibility is not guaranteed. 
        /// The DSIG table may also be invalidated.
        /// </summary>
        LosslessOptimized = 1 << 11,

        /// <summary>
        /// Font converted (produce compatible metrics).
        /// </summary>
        ConvertedFont = 1 << 12,

        /// <summary>
        /// Font optimized for ClearType®. Fonts relying on embedded bitmaps (EBDT) should keep this cleared.
        /// </summary>
        ClearTypeOptimized = 1 << 13,

        /// <summary>
        /// Last Resort font. If set, glyphs in 'cmap' subtables are generic symbolic representations. 
        /// If unset, glyphs represent proper support for code points.
        /// </summary>
        LastResortFont = 1 << 14,

        /// <summary>
        /// Reserved, set to 0.
        /// </summary>
        ReservedBit15 = 1 << 15
    }

    [Flags]
    public enum MacStyleFlags : ushort
    {
        /// <summary>
        /// Bold (if set to 1)
        /// </summary>
        Bold = 1 << 0,

        /// <summary>
        /// Italic (if set to 1)
        /// </summary>
        Italic = 1 << 1,

        /// <summary>
        /// Underline (if set to 1)
        /// </summary>
        Underline = 1 << 2,

        /// <summary>
        /// Outline (if set to 1)
        /// </summary>
        Outline = 1 << 3,

        /// <summary>
        /// Shadow (if set to 1)
        /// </summary>
        Shadow = 1 << 4,

        /// <summary>
        /// Condensed (if set to 1)
        /// </summary>
        Condensed = 1 << 5,

        /// <summary>
        /// Extended (if set to 1)
        /// </summary>
        Extended = 1 << 6,

        /// <summary>
        /// Reserved bits 7–15, should be set to 0
        /// </summary>
        Reserved = 0b111111111 << 7
    }

    /// <summary>
    /// Specifies the glyph directionality of the font.
    /// </summary>
    public enum FontDirectionHintEnum : int
    {
        /// <summary>
        /// Deprecated (Set to 2)
        /// </summary>
        Deprecated = 2,

        /// <summary>
        /// Fully mixed directional glyphs
        /// </summary>
        FullyMixed = 0,

        /// <summary>
        /// Only strongly left to right
        /// </summary>
        StrongLeftToRight = 1,

        /// <summary>
        /// Like StrongLeftToRight but also contains neutrals
        /// </summary>
        StrongLeftToRightWithNeutrals = 2,

        /// <summary>
        /// Only strongly right to left
        /// </summary>
        StrongRightToLeft = -1,

        /// <summary>
        /// Like StrongRightToLeft but also contains neutrals
        /// </summary>
        StrongRightToLeftWithNeutrals = -2
    }

    /// <summary>
    /// Indicates the format of the 'loca' table offsets in a TrueType/OpenType font.
    /// </summary>
    public enum IndexToLocFormatEnum : short
    {
        /// <summary>
        /// Short offsets (16-bit). Offsets divided by 2 are stored in the 'loca' table.
        /// </summary>
        Short = 0,

        /// <summary>
        /// Long offsets (32-bit). Full offsets stored in the 'loca' table.
        /// </summary>
        Long = 1
    }

    /// <summary>
    /// Specifies the format of the glyph data in the 'glyf' table.
    /// </summary>
    public enum GlyphDataFormatEnum : short
    {
        /// <summary>
        /// Current (and only) format defined by the TrueType/OpenType specification.
        /// </summary>
        Current = 0
    }
}
