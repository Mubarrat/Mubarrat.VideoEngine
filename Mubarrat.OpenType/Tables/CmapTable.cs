using Mubarrat.OpenType.CommonTables;

namespace Mubarrat.OpenType.Tables;

public sealed class CmapTable : IOpenTypeTable
{
    public const string TableTag = "cmap";
    public string Tag => TableTag;

    public CmapHeader Header { get; private set; }
    public EncodingRecord[] EncodingRecords { get; private set; } = [];
    public CmapSubtable? PreferredUnicodeSubtable { get; private set; }
    public Format14Subtable? UnicodeVariationSubtable { get; private set; }

    public readonly record struct CmapHeader(ushort Version, ushort NumTables);

    public readonly record struct EncodingRecord(
        ushort PlatformId,
        ushort EncodingId,
        CmapSubtable Subtable);

    public void Parse(ParsedTables tables, OpenTypeReader.TableScope scope)
    {
        ushort version = scope.Reader.ReadUInt16();
        ushort numTables = scope.Reader.ReadUInt16();

        if (version != 0)
            throw new InvalidDataException($"Unsupported cmap version {version}.");

        Header = new CmapHeader(version, numTables);

        EncodingRecords = new EncodingRecord[numTables];

        for (int i = 0; i < numTables; i++)
        {
            ushort platformId = scope.Reader.ReadUInt16();
            ushort encodingId = scope.Reader.ReadUInt16();
            uint offset = scope.Reader.ReadOffset32();

            if (offset == 0)
                throw new InvalidDataException("cmap encoding record subtableOffset cannot be 0.");

            using var subScope = scope.EnterScope(checked((long)offset));
            var subtable = CmapSubtable.Parse(subScope);

            EncodingRecords[i] = new EncodingRecord(platformId, encodingId, subtable);

            if (platformId == 0 && encodingId == 5 && subtable is Format14Subtable format14)
                UnicodeVariationSubtable = format14;
        }

        PreferredUnicodeSubtable = SelectPreferredUnicodeSubtable(EncodingRecords);

        tables.Add(this);
    }

    public bool TryGetGlyphId(uint codePoint, out ushort glyphId)
    {
        if (PreferredUnicodeSubtable is null)
        {
            glyphId = 0;
            return false;
        }

        return PreferredUnicodeSubtable.TryGetGlyphId(codePoint, out glyphId);
    }

    public bool TryGetGlyphId(ushort platformId, ushort encodingId, uint codePoint, out ushort glyphId)
    {
        for (int i = 0; i < EncodingRecords.Length; i++)
        {
            ref readonly var record = ref EncodingRecords[i];

            if (record.PlatformId == platformId && record.EncodingId == encodingId)
                return record.Subtable.TryGetGlyphId(codePoint, out glyphId);
        }

        glyphId = 0;
        return false;
    }

    public bool TryGetVariationGlyphId(uint baseCodePoint, uint variationSelector, out ushort glyphId)
    {
        if (UnicodeVariationSubtable is null)
        {
            glyphId = 0;
            return false;
        }

        return UnicodeVariationSubtable.TryGetVariationGlyphId(baseCodePoint, variationSelector, out glyphId);
    }

    private static CmapSubtable? SelectPreferredUnicodeSubtable(EncodingRecord[] records)
    {
        CmapSubtable? Find(ushort platformId, ushort encodingId)
        {
            for (int i = 0; i < records.Length; i++)
            {
                ref readonly var record = ref records[i];

                if (record.PlatformId == platformId && record.EncodingId == encodingId)
                {
                    if (record.Subtable is Format14Subtable)
                        return null;

                    return record.Subtable;
                }
            }

            return null;
        }

        return Find(3, 10)
            ?? Find(0, 4)
            ?? Find(3, 1)
            ?? Find(0, 3)
            ?? Find(0, 6)
            ?? Find(0, 0)
            ?? Find(0, 1)
            ?? Find(0, 2);
    }

    public abstract record CmapSubtable(ushort Format) : IOpenTypeCommonTable<CmapSubtable>
    {
        public virtual bool TryGetGlyphId(uint codePoint, out ushort glyphId)
        {
            glyphId = 0;
            return false;
        }

        public static CmapSubtable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            ushort format = scope.Reader.ReadUInt16();

            return format switch
            {
                0 => Format0Subtable.Parse(scope),
                2 => Format2Subtable.Parse(scope),
                4 => Format4Subtable.Parse(scope),
                6 => Format6Subtable.Parse(scope),
                8 => Format8Subtable.Parse(scope),
                10 => Format10Subtable.Parse(scope),
                12 => Format12Subtable.Parse(scope),
                13 => Format13Subtable.Parse(scope),
                14 => Format14Subtable.Parse(scope),
                _ => throw new NotSupportedException($"Unsupported cmap format {format}.")
            };
        }
    }

    public sealed record Format0Subtable(
        ushort Format,
        ushort Length,
        ushort Language,
        byte[] GlyphIdArray) : CmapSubtable(Format)
    {
        internal static CmapSubtable Parse(OpenTypeReader.TableScope scope)
        {
            ushort length = scope.Reader.ReadUInt16();
            ushort language = scope.Reader.ReadUInt16();

            var glyphIdArray = scope.Reader.ReadBytes(256);

            return new Format0Subtable(0, length, language, glyphIdArray);
        }

        public override bool TryGetGlyphId(uint codePoint, out ushort glyphId)
        {
            if (codePoint > 0xFF)
            {
                glyphId = 0;
                return false;
            }

            glyphId = GlyphIdArray[(int)codePoint];
            return true;
        }
    }

    public readonly record struct SubHeader(
        ushort FirstCode,
        ushort EntryCount,
        short IdDelta,
        ushort IdRangeOffset);

    public sealed record Format2Subtable(
        ushort Format,
        ushort Length,
        ushort Language,
        ushort[] SubHeaderKeys,
        SubHeader[] SubHeaders,
        ushort[] GlyphIdArray) : CmapSubtable(Format)
    {
        internal static CmapSubtable Parse(OpenTypeReader.TableScope scope)
        {
            var r = scope.Reader;

            ushort length = r.ReadUInt16();
            ushort language = r.ReadUInt16();

            var subHeaderKeys = r.ReadUInt16Array(256);

            ushort maxKey = 0;
            for (int i = 0; i < subHeaderKeys.Length; i++)
                if (subHeaderKeys[i] > maxKey)
                    maxKey = subHeaderKeys[i];

            if ((maxKey & 7) != 0)
                throw new InvalidDataException("Invalid cmap format 2 subHeaderKeys.");

            int subHeaderCount = (maxKey / 8) + 1;
            var subHeaders = new SubHeader[subHeaderCount];

            for (int i = 0; i < subHeaderCount; i++)
            {
                subHeaders[i] = new SubHeader(
                    r.ReadUInt16(),
                    r.ReadUInt16(),
                    r.ReadInt16(),
                    r.ReadUInt16());
            }

            int bytesRead = checked((int)(r.Position - scope.Base));
            int bytesRemaining = checked((int)length) - bytesRead;

            if (bytesRemaining < 0 || (bytesRemaining & 1) != 0)
                throw new InvalidDataException("Invalid cmap format 2 length.");

            var glyphIdArray = r.ReadUInt16Array(bytesRemaining / 2);

            return new Format2Subtable(2, length, language, subHeaderKeys, subHeaders, glyphIdArray);
        }

        public override bool TryGetGlyphId(uint codePoint, out ushort glyphId)
        {
            if (codePoint > 0xFFFF)
            {
                glyphId = 0;
                return false;
            }

            byte highByte = (byte)(codePoint >> 8);
            byte lowByte = (byte)codePoint;

            int subHeaderIndex = SubHeaderKeys[highByte] / 8;
            if ((uint)subHeaderIndex >= (uint)SubHeaders.Length)
            {
                glyphId = 0;
                return false;
            }

            ref readonly var subHeader = ref SubHeaders[subHeaderIndex];

            int firstCode = subHeader.FirstCode;
            int entryCount = subHeader.EntryCount;

            if (lowByte < firstCode || lowByte >= firstCode + entryCount)
            {
                glyphId = 0;
                return false;
            }

            if (subHeader.IdRangeOffset == 0)
            {
                glyphId = unchecked((ushort)(lowByte + subHeader.IdDelta));
                return true;
            }

            int glyphIndex = (subHeader.IdRangeOffset / 2)
                + (lowByte - firstCode)
                + (subHeaderIndex * 4)
                + 3
                - (SubHeaders.Length * 4);

            if ((uint)glyphIndex >= (uint)GlyphIdArray.Length)
            {
                glyphId = 0;
                return false;
            }

            ushort mapped = GlyphIdArray[glyphIndex];
            if (mapped != 0)
                mapped = unchecked((ushort)(mapped + subHeader.IdDelta));

            glyphId = mapped;
            return true;
        }
    }

    public sealed record Format4Subtable(
        ushort Format,
        ushort Length,
        ushort Language,
        ushort SegCountX2,
        ushort[] EndCodes,
        ushort[] StartCodes,
        short[] IdDeltas,
        ushort[] IdRangeOffsets,
        ushort[] GlyphIdArray) : CmapSubtable(Format)
    {
        internal static CmapSubtable Parse(OpenTypeReader.TableScope scope)
        {
            var r = scope.Reader;

            ushort length = r.ReadUInt16();
            ushort language = r.ReadUInt16();
            ushort segCountX2 = r.ReadUInt16();
            ushort searchRange = r.ReadUInt16();
            ushort entrySelector = r.ReadUInt16();
            ushort rangeShift = r.ReadUInt16();

            if ((segCountX2 & 1) != 0)
                throw new InvalidDataException("Invalid cmap format 4 segCountX2.");

            int segCount = segCountX2 / 2;

            var endCodes = r.ReadUInt16Array(segCount);

            ushort reservedPad = r.ReadUInt16();
            if (reservedPad != 0)
                throw new InvalidDataException("Invalid cmap format 4 reservedPad.");

            var startCodes = r.ReadUInt16Array(segCount);

            var idDeltas = new short[segCount];
            for (int i = 0; i < segCount; i++)
                idDeltas[i] = r.ReadInt16();

            var idRangeOffsets = r.ReadUInt16Array(segCount);

            int bytesRead = checked((int)(r.Position - scope.Base));
            int bytesRemaining = checked((int)length) - bytesRead;

            if (bytesRemaining < 0 || (bytesRemaining & 1) != 0)
                throw new InvalidDataException("Invalid cmap format 4 length.");

            var glyphIdArray = r.ReadUInt16Array(bytesRemaining / 2);

            return new Format4Subtable(4, length, language, segCountX2, endCodes, startCodes, idDeltas, idRangeOffsets, glyphIdArray);
        }

        public override bool TryGetGlyphId(uint codePoint, out ushort glyphId)
        {
            if (codePoint > 0xFFFF)
            {
                glyphId = 0;
                return false;
            }

            ushort code = (ushort)codePoint;

            int lo = 0;
            int hi = EndCodes.Length - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;

                if (code <= EndCodes[mid])
                    hi = mid - 1;
                else
                    lo = mid + 1;
            }

            int i = lo;
            if ((uint)i >= (uint)EndCodes.Length)
            {
                glyphId = 0;
                return false;
            }

            if (code < StartCodes[i] || code > EndCodes[i])
            {
                glyphId = 0;
                return false;
            }

            if (IdRangeOffsets[i] == 0)
            {
                glyphId = unchecked((ushort)(code + IdDeltas[i]));
                return true;
            }

            int glyphIndex = (IdRangeOffsets[i] / 2)
                + (code - StartCodes[i])
                + i
                - EndCodes.Length;

            if ((uint)glyphIndex >= (uint)GlyphIdArray.Length)
            {
                glyphId = 0;
                return false;
            }

            ushort mapped = GlyphIdArray[glyphIndex];
            if (mapped != 0)
                mapped = unchecked((ushort)(mapped + IdDeltas[i]));

            glyphId = mapped;
            return true;
        }
    }

    public sealed record Format6Subtable(
        ushort Format,
        ushort Length,
        ushort Language,
        ushort FirstCode,
        ushort EntryCount,
        ushort[] GlyphIdArray) : CmapSubtable(Format)
    {
        internal static CmapSubtable Parse(OpenTypeReader.TableScope scope)
        {
            var r = scope.Reader;

            ushort length = r.ReadUInt16();
            ushort language = r.ReadUInt16();
            ushort firstCode = r.ReadUInt16();
            ushort entryCount = r.ReadUInt16();

            var glyphIdArray = r.ReadUInt16Array(entryCount);

            return new Format6Subtable(6, length, language, firstCode, entryCount, glyphIdArray);
        }

        public override bool TryGetGlyphId(uint codePoint, out ushort glyphId)
        {
            if (codePoint < FirstCode)
            {
                glyphId = 0;
                return false;
            }

            uint rel = codePoint - FirstCode;
            if (rel >= EntryCount)
            {
                glyphId = 0;
                return false;
            }

            glyphId = GlyphIdArray[(int)rel];
            return true;
        }
    }

    public readonly record struct SequentialMapGroup(
        uint StartCharCode,
        uint EndCharCode,
        uint StartGlyphID);

    public sealed record Format8Subtable(
        ushort Format,
        ushort Reserved,
        uint Length,
        uint Language,
        byte[] Is32,
        SequentialMapGroup[] Groups) : CmapSubtable(Format)
    {
        internal static CmapSubtable Parse(OpenTypeReader.TableScope scope)
        {
            var r = scope.Reader;

            ushort reserved = r.ReadUInt16();
            uint length = r.ReadUInt32();
            uint language = r.ReadUInt32();

            var is32 = r.ReadBytes(8192);

            uint groupCount = r.ReadUInt32();
            var groups = new SequentialMapGroup[checked((int)groupCount)];

            for (int i = 0; i < groups.Length; i++)
            {
                uint start = r.ReadUInt32();
                uint end = r.ReadUInt32();
                uint startGlyph = r.ReadUInt32();
                groups[i] = new SequentialMapGroup(start, end, startGlyph);
            }

            return new Format8Subtable(8, reserved, length, language, is32, groups);
        }

        public override bool TryGetGlyphId(uint codePoint, out ushort glyphId)
        {
            int lo = 0;
            int hi = Groups.Length - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ref readonly var group = ref Groups[mid];

                if (codePoint < group.StartCharCode)
                {
                    hi = mid - 1;
                    continue;
                }

                if (codePoint > group.EndCharCode)
                {
                    lo = mid + 1;
                    continue;
                }

                ulong mapped = (ulong)group.StartGlyphID + (codePoint - group.StartCharCode);
                glyphId = unchecked((ushort)mapped);
                return true;
            }

            glyphId = 0;
            return false;
        }
    }

    public sealed record Format10Subtable(
        ushort Format,
        ushort Reserved,
        uint Length,
        uint Language,
        uint StartCharCode,
        uint NumChars,
        ushort[] GlyphIdArray) : CmapSubtable(Format)
    {
        internal static CmapSubtable Parse(OpenTypeReader.TableScope scope)
        {
            var r = scope.Reader;

            ushort reserved = r.ReadUInt16();
            uint length = r.ReadUInt32();
            uint language = r.ReadUInt32();
            uint startCharCode = r.ReadUInt32();
            uint numChars = r.ReadUInt32();

            var glyphIdArray = new ushort[checked((int)numChars)];
            r.ReadUInt16Array(glyphIdArray);

            return new Format10Subtable(10, reserved, length, language, startCharCode, numChars, glyphIdArray);
        }

        public override bool TryGetGlyphId(uint codePoint, out ushort glyphId)
        {
            if (codePoint < StartCharCode)
            {
                glyphId = 0;
                return false;
            }

            uint rel = codePoint - StartCharCode;
            if (rel >= NumChars)
            {
                glyphId = 0;
                return false;
            }

            glyphId = GlyphIdArray[(int)rel];
            return true;
        }
    }

    public sealed record Format12Subtable(
        ushort Format,
        ushort Reserved,
        uint Length,
        uint Language,
        SequentialMapGroup[] Groups) : CmapSubtable(Format)
    {
        internal static CmapSubtable Parse(OpenTypeReader.TableScope scope)
        {
            var r = scope.Reader;

            ushort reserved = r.ReadUInt16();
            uint length = r.ReadUInt32();
            uint language = r.ReadUInt32();
            uint groupCount = r.ReadUInt32();

            var groups = new SequentialMapGroup[checked((int)groupCount)];

            for (int i = 0; i < groups.Length; i++)
            {
                uint start = r.ReadUInt32();
                uint end = r.ReadUInt32();
                uint startGlyph = r.ReadUInt32();
                groups[i] = new SequentialMapGroup(start, end, startGlyph);
            }

            return new Format12Subtable(12, reserved, length, language, groups);
        }

        public override bool TryGetGlyphId(uint codePoint, out ushort glyphId)
        {
            int lo = 0;
            int hi = Groups.Length - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ref readonly var group = ref Groups[mid];

                if (codePoint < group.StartCharCode)
                {
                    hi = mid - 1;
                    continue;
                }

                if (codePoint > group.EndCharCode)
                {
                    lo = mid + 1;
                    continue;
                }

                ulong mapped = (ulong)group.StartGlyphID + (codePoint - group.StartCharCode);
                glyphId = unchecked((ushort)mapped);
                return true;
            }

            glyphId = 0;
            return false;
        }
    }

    public readonly record struct ConstantMapGroup(
        uint StartCharCode,
        uint EndCharCode,
        uint GlyphID);

    public sealed record Format13Subtable(
        ushort Format,
        ushort Reserved,
        uint Length,
        uint Language,
        ConstantMapGroup[] Groups) : CmapSubtable(Format)
    {
        internal static CmapSubtable Parse(OpenTypeReader.TableScope scope)
        {
            var r = scope.Reader;

            ushort reserved = r.ReadUInt16();
            uint length = r.ReadUInt32();
            uint language = r.ReadUInt32();
            uint groupCount = r.ReadUInt32();

            var groups = new ConstantMapGroup[checked((int)groupCount)];

            for (int i = 0; i < groups.Length; i++)
            {
                uint start = r.ReadUInt32();
                uint end = r.ReadUInt32();
                uint glyphId = r.ReadUInt32();
                groups[i] = new ConstantMapGroup(start, end, glyphId);
            }

            return new Format13Subtable(13, reserved, length, language, groups);
        }

        public override bool TryGetGlyphId(uint codePoint, out ushort glyphId)
        {
            int lo = 0;
            int hi = Groups.Length - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ref readonly var group = ref Groups[mid];

                if (codePoint < group.StartCharCode)
                {
                    hi = mid - 1;
                    continue;
                }

                if (codePoint > group.EndCharCode)
                {
                    lo = mid + 1;
                    continue;
                }

                glyphId = unchecked((ushort)group.GlyphID);
                return true;
            }

            glyphId = 0;
            return false;
        }
    }

    public sealed record Format14Subtable(
        ushort Format,
        uint Length,
        VariationSelectorRecord[] Records) : CmapSubtable(Format)
    {
        internal static CmapSubtable Parse(OpenTypeReader.TableScope scope)
        {
            var r = scope.Reader;

            uint length = r.ReadUInt32();
            uint recordCount = r.ReadUInt32();

            var records = new VariationSelectorRecord[checked((int)recordCount)];

            for (int i = 0; i < records.Length; i++)
            {
                uint varSelector = r.ReadUInt24();
                uint defaultOffset = r.ReadUInt32();
                uint nonDefaultOffset = r.ReadUInt32();

                DefaultUVSTable? defaultUVS = defaultOffset != 0
                    ? scope.ParseCommonTable<DefaultUVSTable>(checked((long)defaultOffset))
                    : null;

                NonDefaultUVSTable? nonDefaultUVS = nonDefaultOffset != 0
                    ? scope.ParseCommonTable<NonDefaultUVSTable>(checked((long)nonDefaultOffset))
                    : null;

                records[i] = new VariationSelectorRecord(varSelector, defaultUVS, nonDefaultUVS);
            }

            return new Format14Subtable(14, length, records);
        }

        public bool TryGetVariationGlyphId(uint baseCodePoint, uint variationSelector, out ushort glyphId)
        {
            int lo = 0;
            int hi = Records.Length - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                uint selector = Records[mid].VariationSelector;

                if (variationSelector < selector)
                {
                    hi = mid - 1;
                    continue;
                }

                if (variationSelector > selector)
                {
                    lo = mid + 1;
                    continue;
                }

                ref readonly var record = ref Records[mid];

                if (record.NonDefaultUVS?.TryGetGlyphId(baseCodePoint, out glyphId) == true)
                {
                    return true;
                }

                if (record.DefaultUVS?.Contains(baseCodePoint) == true)
                {
                    glyphId = 0;
                    return false;
                }

                glyphId = 0;
                return false;
            }

            glyphId = 0;
            return false;
        }
    }

    public readonly record struct VariationSelectorRecord(
        uint VariationSelector,
        DefaultUVSTable? DefaultUVS,
        NonDefaultUVSTable? NonDefaultUVS);

    public readonly record struct UnicodeRange(
        uint StartUnicodeValue,
        byte AdditionalCount)
    {
        public bool Contains(uint codePoint)
        {
            uint end = StartUnicodeValue + AdditionalCount;
            return codePoint >= StartUnicodeValue && codePoint <= end;
        }
    }

    public readonly record struct DefaultUVSTable(
        UnicodeRange[] Ranges) : IOpenTypeCommonTable<DefaultUVSTable>
    {
        public static DefaultUVSTable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            uint count = scope.Reader.ReadUInt32();
            var ranges = new UnicodeRange[checked((int)count)];

            for (int i = 0; i < ranges.Length; i++)
            {
                uint start = scope.Reader.ReadUInt24();
                byte additionalCount = scope.Reader.ReadUInt8();
                ranges[i] = new UnicodeRange(start, additionalCount);
            }

            return new DefaultUVSTable(ranges);
        }

        public bool Contains(uint codePoint)
        {
            int lo = 0;
            int hi = Ranges.Length - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ref readonly var range = ref Ranges[mid];

                if (codePoint < range.StartUnicodeValue)
                {
                    hi = mid - 1;
                    continue;
                }

                uint end = range.StartUnicodeValue + range.AdditionalCount;
                if (codePoint > end)
                {
                    lo = mid + 1;
                    continue;
                }

                return true;
            }

            return false;
        }
    }

    public readonly record struct UVSMapping(
        uint UnicodeValue,
        ushort GlyphID);

    public readonly record struct NonDefaultUVSTable(
        UVSMapping[] Mappings) : IOpenTypeCommonTable<NonDefaultUVSTable>
    {
        public static NonDefaultUVSTable Parse(OpenTypeReader.TableScope scope, object? param = null)
        {
            uint count = scope.Reader.ReadUInt32();
            var mappings = new UVSMapping[checked((int)count)];

            for (int i = 0; i < mappings.Length; i++)
            {
                uint unicodeValue = scope.Reader.ReadUInt24();
                ushort glyphId = scope.Reader.ReadUInt16();
                mappings[i] = new UVSMapping(unicodeValue, glyphId);
            }

            return new NonDefaultUVSTable(mappings);
        }

        public bool TryGetGlyphId(uint codePoint, out ushort glyphId)
        {
            int lo = 0;
            int hi = Mappings.Length - 1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                uint value = Mappings[mid].UnicodeValue;

                if (codePoint < value)
                {
                    hi = mid - 1;
                    continue;
                }

                if (codePoint > value)
                {
                    lo = mid + 1;
                    continue;
                }

                glyphId = Mappings[mid].GlyphID;
                return true;
            }

            glyphId = 0;
            return false;
        }
    }
}
