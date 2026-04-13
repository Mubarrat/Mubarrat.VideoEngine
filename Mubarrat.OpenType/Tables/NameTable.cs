using System.Text;

namespace Mubarrat.OpenType.Tables;

public sealed class NameTable : IOpenTypeTable
{
    static NameTable() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public string Tag => "name";

    public NameTableHeader Header { get; private set; } = default;
    public NameRecord[] NameRecords { get; private set; } = [];
    public LangTagRecord[] LangTagRecords { get; private set; } = [];

    public readonly record struct NameTableHeader(ushort Version, ushort Count, ushort StorageOffset);

    public enum NameId : ushort
    {
        CopyrightNotice = 0,
        FontFamilyName = 1,
        FontSubfamilyName = 2,
        UniqueFontIdentifier = 3,
        FullFontName = 4,
        VersionString = 5,
        PostScriptName = 6,
        Trademark = 7,
        ManufacturerName = 8,
        Designer = 9,
        Description = 10,
        VendorUrl = 11,
        DesignerUrl = 12,
        LicenseDescription = 13,
        LicenseInfoUrl = 14,
        Reserved15 = 15,
        TypographicFamilyName = 16,
        TypographicSubfamilyName = 17,
        CompatibleFullMacintoshOnly = 18,
        SampleText = 19,
        PostScriptCidFindFontName = 20,
        WwsFamilyName = 21,
        WwsSubfamilyName = 22,
        LightBackgroundPalette = 23,
        DarkBackgroundPalette = 24,
        VariationsPostScriptNamePrefix = 25
    }

    public enum PlatformId : ushort
    {
        Unicode = 0,
        Macintosh = 1,
        Windows = 3
    }

    public enum UnicodeEncodingId : ushort
    {
        Unicode10SemanticsDeprecated = 0,
        Unicode11SemanticsDeprecated = 1,
        ISO10646SemanticsDeprecated = 2,
        Unicode20AndOnwardsBmpOnly = 3,
        Unicode20AndOnwardsFullRepertoire = 4
    }

    public enum WindowsEncodingId : ushort
    {
        Symbol = 0,
        UnicodeBmp = 1,
        ShiftJis = 2,
        Prc = 3,
        Big5 = 4,
        Wansung = 5,
        Johab = 6,
        Reserved7 = 7,
        Reserved8 = 8,
        Reserved9 = 9,
        UnicodeFullRepertoire = 10
    }

    public enum MacintoshEncodingId : ushort
    {
        Roman = 0,
        Japanese = 1,
        ChineseTraditional = 2,
        Korean = 3,
        Arabic = 4,
        Hebrew = 5,
        Greek = 6,
        Russian = 7,
        RSymbol = 8,
        Devanagari = 9,
        Gurmukhi = 10,
        Gujarati = 11,
        Odia = 12,
        Bangla = 13,
        Tamil = 14,
        Telugu = 15,
        Kannada = 16,
        Malayalam = 17,
        Sinhalese = 18,
        Burmese = 19,
        Khmer = 20,
        Thai = 21,
        Laotian = 22,
        Georgian = 23,
        Armenian = 24,
        ChineseSimplified = 25,
        Tibetan = 26,
        Mongolian = 27,
        Geez = 28,
        Slavic = 29,
        Vietnamese = 30,
        Sindhi = 31,
        Uninterpreted = 32
    }

    public readonly record struct NameRecord(
        ushort PlatformID,
        ushort EncodingID,
        ushort LanguageID,
        NameId NameID,
        ushort Length,
        ushort StringOffset,
        string Value,
        string? LanguageTag);

    public readonly record struct LangTagRecord(string Value);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scoped)
    {
        var reader = scoped.Reader;

        ushort version = reader.ReadUInt16();
        ushort count = reader.ReadUInt16();
        ushort storageOffset = reader.ReadUInt16();

        Header = new NameTableHeader(version, count, storageOffset);

        var rawRecords = new (ushort PlatformID, ushort EncodingID, ushort LanguageID, NameId NameID, ushort Length, ushort StringOffset)[count];
        for (int i = 0; i < rawRecords.Length; i++)
        {
            rawRecords[i] = (
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16(),
                (NameId)reader.ReadUInt16(),
                reader.ReadUInt16(),
                reader.ReadUInt16());
        }

        LangTagRecord[] langTags = [];
        if (version == 1)
        {
            ushort langTagCount = reader.ReadUInt16();
            langTags = new LangTagRecord[langTagCount];

            for (int i = 0; i < langTags.Length; i++)
            {
                ushort length = reader.ReadUInt16();
                ushort offset = reader.ReadUInt16();

                langTags[i] = new LangTagRecord(ReadStorageString(scoped, storageOffset, offset, length, platformId: 0, encodingId: 0));
            }
        }
        else if (version != 0)
        {
            throw new InvalidDataException($"Unsupported name table version {version}.");
        }

        NameRecords = new NameRecord[rawRecords.Length];

        for (int i = 0; i < rawRecords.Length; i++)
        {
            var raw = rawRecords[i];
            string value = ReadStorageString(scoped, storageOffset, raw.StringOffset, raw.Length, raw.PlatformID, raw.EncodingID);
            string? languageTag = ResolveLanguageTag(raw.LanguageID, langTags);

            NameRecords[i] = new NameRecord(
                raw.PlatformID,
                raw.EncodingID,
                raw.LanguageID,
                raw.NameID,
                raw.Length,
                raw.StringOffset,
                value,
                languageTag);
        }

        LangTagRecords = langTags;
        tables.Add(this);
    }

    public bool TryGetName(ushort platformId, ushort encodingId, ushort languageId, NameId nameId, out string value)
    {
        foreach (var record in NameRecords)
        {
            if (record.PlatformID == platformId &&
                record.EncodingID == encodingId &&
                record.LanguageID == languageId &&
                record.NameID == nameId)
            {
                value = record.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public bool TryGetName(NameId nameId, out string value)
    {
        foreach (var record in NameRecords)
        {
            if (record.NameID == nameId)
            {
                value = record.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public string? GetLanguageTag(ushort languageId)
    {
        if (languageId < 0x8000)
            return null;

        int index = languageId - 0x8000;
        return (uint)index < (uint)LangTagRecords.Length
            ? LangTagRecords[index].Value
            : null;
    }

    private static string? ResolveLanguageTag(ushort languageId, LangTagRecord[] langTags)
    {
        if (languageId < 0x8000)
            return null;

        int index = languageId - 0x8000;
        return (uint)index < (uint)langTags.Length
            ? langTags[index].Value
            : null;
    }

    private static string ReadStorageString(
        OpenTypeReader.TableScope scoped,
        ushort storageOffset,
        ushort stringOffset,
        ushort length,
        ushort platformId,
        ushort encodingId)
    {
        if (length == 0)
            return string.Empty;

        ulong absolute = (ulong)storageOffset + stringOffset;
        if (absolute > long.MaxValue)
            throw new InvalidDataException("name string offset overflow.");

        using var stringScope = scoped.EnterScope((long)absolute);
        byte[] bytes = stringScope.Reader.ReadBytes(length);

        return DecodeString(bytes, platformId, encodingId);
    }

    private static string DecodeString(byte[] bytes, ushort platformId, ushort encodingId)
    {
        if (platformId == (ushort)PlatformId.Unicode)
            return Encoding.BigEndianUnicode.GetString(bytes);

        if (platformId == (ushort)PlatformId.Windows)
        {
            return encodingId switch
            {
                (ushort)WindowsEncodingId.Big5 => GetEncoding(950).GetString(bytes),
                (ushort)WindowsEncodingId.Prc => GetEncoding(936).GetString(bytes),
                (ushort)WindowsEncodingId.Wansung => GetEncoding(949).GetString(bytes),
                (ushort)WindowsEncodingId.UnicodeBmp => Encoding.BigEndianUnicode.GetString(bytes),
                (ushort)WindowsEncodingId.UnicodeFullRepertoire => Encoding.BigEndianUnicode.GetString(bytes),
                _ => Encoding.BigEndianUnicode.GetString(bytes)
            };
        }

        if (platformId == (ushort)PlatformId.Macintosh)
        {
            return encodingId switch
            {
                (ushort)MacintoshEncodingId.Roman => GetEncoding(10000).GetString(bytes),
                (ushort)MacintoshEncodingId.Japanese => GetEncoding(932).GetString(bytes),
                (ushort)MacintoshEncodingId.ChineseTraditional => GetEncoding(950).GetString(bytes),
                (ushort)MacintoshEncodingId.Korean => GetEncoding(949).GetString(bytes),
                (ushort)MacintoshEncodingId.Arabic => GetEncoding(1256).GetString(bytes),
                (ushort)MacintoshEncodingId.Hebrew => GetEncoding(1255).GetString(bytes),
                (ushort)MacintoshEncodingId.Greek => GetEncoding(1253).GetString(bytes),
                (ushort)MacintoshEncodingId.Russian => GetEncoding(1251).GetString(bytes),
                (ushort)MacintoshEncodingId.ChineseSimplified => GetEncoding(936).GetString(bytes),
                _ => GetEncoding(10000).GetString(bytes)
            };
        }

        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    private static Encoding GetEncoding(int codePage)
    {
        try
        {
            return Encoding.GetEncoding(codePage);
        }
        catch
        {
            return Encoding.BigEndianUnicode;
        }
    }
}
