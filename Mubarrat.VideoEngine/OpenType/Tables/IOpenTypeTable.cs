namespace Mubarrat.VideoEngine.OpenType.Tables;

public interface IOpenTypeTable
{
    string Tag { get; }
    void Parse(ParsedTables tables, OpenTypeReader.TableScope scope);

    public static IOpenTypeTable? GetEmptyTableFromTag(string tag) => tag switch
    {
        "avar" => new AvarTable(),
        "BASE" => new BaseTable(),
        "CBDT" => new CbdtTable(),
        "CBLC" => new CblcTable(),
        "CFF " => new CffTable(),
        "CFF2" => new Cff2Table(),
        "cmap" => new CmapTable(),
        "COLR" => new ColrTable(),
        "CPAL" => new CpalTable(),
        "cvar" => new CvarTable(),
        "cvt " => new CvtTable(),
        "DSIG" => new DsigTable(),
        "EBDT" => new EbdtTable(),
        "EBLC" => new EblcTable(),
        "EBSC" => new EbscTable(),
        "fpgm" => new FpgmTable(),
        "fvar" => new FvarTable(),
        "gasp" => new GaspTable(),
        "GDEF" => new GdefTable(),
        "glyf" => new GlyfTable(),
        "GPOS" => new GposTable(),
        "GSUB" => new GsubTable(),
        "gvar" => new GvarTable(),
        "hdmx" => new HdmxTable(),
        "head" => new HeadTable(),
        "hhea" => new HheaTable(),
        "hmtx" => new HmtxTable(),
        "HVAR" => new HvarTable(),
        "JSTF" => new JstfTable(),
        "kern" => new KernTable(),
        "loca" => new LocaTable(),
        "LTSH" => new LtshTable(),
        "MATH" => new MathTable(),
        "maxp" => new MaxpTable(),
        "MERG" => new MergTable(),
        "meta" => new MetaTable(),
        "MVAR" => new MvarTable(),
        "name" => new NameTable(),
        "OS/2" => new Os2Table(),
        "PCLT" => new PcltTable(),
        "post" => new PostTable(),
        "prep" => new PrepTable(),
        "sbix" => new SbixTable(),
        "STAT" => new StatTable(),
        "SVG " => new SvgTable(),
        "VDMX" => new VdmxTable(),
        "vhea" => new VheaTable(),
        "vmtx" => new VmtxTable(),
        "VORG" => new VorgTable(),
        "VVAR" => new VvarTable(),
        _ => null
    };
}
