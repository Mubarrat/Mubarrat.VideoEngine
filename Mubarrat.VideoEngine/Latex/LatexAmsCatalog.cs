namespace Mubarrat.VideoEngine.Latex;

public enum LatexPackage
{
    Core,
    AmsMath,
    AmsSymb,
    AmsFonts,
    Unknown
}

internal static class LatexAmsCatalog
{
    private static readonly HashSet<string> AmsMathCommands = BuildSet("""
DeclareMathOperator
DeclareMathOperator*
allowdisplaybreaks
bmod
boxed
cfrac
dbinom
ddots
dddot
ddddot
dfrac
displaybreak
dots
dotsb
dotsc
dotsi
dotsm
dotso
dotseq
eqref
genfrac
hdotsfor
iiiint
iiint
iint
idotsint
intertext
leftroot
lvert
lVert
mathbb
mod
negmedspace
negthickspace
negthinspace
nobreakdash
nolimits
nonumber
notag
numberwithin
operatorname
operatorname*
operatornamewithlimits
overleftarrow
overleftrightarrow
overrightarrow
overset
pmod
pod
raisetag
rvert
rVert
sideset
smashoperator
substack
tag
tag*
tfrac
text
textnormal
underleftarrow
underleftrightarrow
underrightarrow
underset
uproot
varinjlim
varliminf
varlimsup
varprojlim
xleftarrow
xleftrightarrow
xLeftarrow
xLeftrightarrow
xmapsto
xrightarrow
xRightarrow
xhookleftarrow
xhookrightarrow
xlongequal
implies
impliedby
iff
coloneqq
eqqcolon
coloneq
eqcolon
overset
underset
sfrac
binom
tbinom
dmat
smallmatrix
""");

    private static readonly HashSet<string> AmsMathEnvironments = BuildSet("""
align
align*
aligned
alignedat
alignedat*
alignat
alignat*
array
Bmatrix
Bmatrix*
bmatrix
bmatrix*
cases
eqnarray
eqnarray*
equation
equation*
flalign
flalign*
gather
gather*
gathered
matrix
matrix*
multline
multline*
pmatrix
pmatrix*
split
split*
subequations
Vmatrix
Vmatrix*
vmatrix
vmatrix*
xalignat
xalignat*
xxalignat
""");

    private static readonly HashSet<string> AmsSymbCommands = BuildSet("""
Aleph
Bbbk
Beth
Box
Bumpeq
Cap
Cup
Daleth
Diamond
Finv
Game
Geq
GreaterEqual
GreaterEqualLess
GreaterFullEqual
GreaterGreater
GreaterLess
GreaterSlantEqual
GreaterTilde
Im
Join
Leftarrow
LeftRightArrow
Leftrightarrow
Lleftarrow
Longleftarrow
Longleftrightarrow
Longmapsfrom
Longmapsto
Longrightarrow
Mapsfrom
Mapsto
Mho
NLeftarrow
NLeftrightarrow
NRightarrow
Pr
Re
Rightarrow
Rrightarrow
Subset
Supset
Therefore
VDash
Vvdash
Xi
angle
approxeq
backepsilon
backprime
backsim
backsimeq
barwedge
because
between
bigblacktriangleup
bigblacktriangledown
bigcap
bigcup
bigcurlyvee
bigcurlywedge
biginterleave
bigodot
bigoplus
bigotimes
bigsqcup
bigstar
bigtriangledown
blacklozenge
blacksquare
blacktriangle
blacktriangledown
blacktriangleleft
blacktriangleright
bot
bowtie
boxdot
boxminus
boxplus
boxtimes
bumpeq
capdot
centerdot
checkmark
circledR
circledS
circledast
circledcirc
circleddash
complement
curlyeqprec
curlyeqsucc
curlyvee
curlywedge
dashleftarrow
dashrightarrow
dashv
ddbar
ddotseq
diamondsuit
digamma
divides
doteqdot
dotplus
doublebarwedge
downharpoonleft
downharpoonright
downupharpoons
dupharpoonleft
dupharpoonright
ell
eqcirc
eqsim
fallingdotseq
forall
gimel
gnapprox
gneq
gneqq
gnsim
gtrapprox
gtreqless
gtreqqless
gtrless
gtrsim
gvertneqq
hbar
hslash
intercal
leftarrowtail
leftleftarrows
leftrightarrows
leftrightarroweq
leftrightharpoons
leftrightsquigarrow
leftthreetimes
leqslant
lessapprox
lesseqgtr
lesseqqgtr
lessgtr
lesssim
lfloor
lhd
llcorner
llless
lnapprox
lneq
lneqq
lnsim
lozenge
lrcorner
ltimes
lvertneqq
measuredangle
mho
mid
models
multimap
nLeftarrow
nLeftrightarrow
nRightarrow
nVDash
nVdash
nabla
nbacksim
nbumpeq
ncong
nexists
ngeq
ngeqq
ngeqslant
ngtr
nleq
nleqq
nleqslant
nless
nmid
nparallel
nprec
npreceq
nshortmid
nshortparallel
nsim
nsubseteq
nsubseteqq
nsucc
nsucceq
nsupseteq
nsupseteqq
ntriangleleft
ntrianglelefteq
ntriangleright
ntrianglerighteq
nvDash
nvdash
nwarrow
odot
ominus
oplus
oslash
otimes
owns
parallel
pitchfork
precapprox
preccurlyeq
preceq
precnapprox
precneq
precneqq
precsim
prime
rhd
rightarrowtail
rightleftarrows
rightleftharpoons
rightthreetimes
risingdotseq
rtimes
shortmid
shortparallel
smallfrown
smallsmile
smeq
spadesuit
sphericalangle
square
subset
subsetapprox
subsetneq
subsetneqq
subseteqq
subseteq
subseteqq
succapprox
succcurlyeq
succeq
succnapprox
succneq
succneqq
succsim
supset
supsetapprox
supsetneq
supsetneqq
supseteqq
supseteq
therefore
thicksim
thickapprox
threepartdef
times
top
triangle
triangledown
triangleleft
trianglelefteq
trianglelefteqslant
triangleq
triangleright
trianglerighteq
trianglerighteqslant
twoheadleftarrow
twoheadrightarrow
ulcorner
uparrow
downarrow
updownarrow
upuparrows
urcorner
varDelta
varGamma
varLambda
varOmega
varPhi
varPsi
varSigma
varTheta
varUpsilon
varXi
varnothing
varpropto
varsubsetneq
varsubsetneqq
varsupsetneq
varsupsetneqq
vartheta
vartriangleleft
vartriangleright
varkappa
veebar
vcentcolon
wedgeq
wr
leadsto
lessdot
gtrdot
lll
ggg
restriction
""");

    private static readonly HashSet<string> AmsFontsCommands = BuildSet("""
Bbb
Bbbk
bold
frak
mathbb
mathfrak
mathcal
mathscr
mathbf
mathsf
mathtt
mathrm
mathit
mathnormal
""");

    private static readonly HashSet<string> AmsSymbEnvironments = BuildSet(string.Empty);

    internal static LatexPackage ResolveCommandPackage(string commandName)
    {
        commandName = NormalizeLatexName(commandName);
        if (string.IsNullOrWhiteSpace(commandName))
            return LatexPackage.Unknown;

        if (ContainsName(AmsMathCommands, commandName))
            return LatexPackage.AmsMath;
        if (ContainsName(AmsSymbCommands, commandName))
            return LatexPackage.AmsSymb;
        if (ContainsName(AmsFontsCommands, commandName))
            return LatexPackage.AmsFonts;

        return LatexPackage.Unknown;
    }

    internal static LatexPackage ResolveEnvironmentPackage(string environmentName)
    {
        environmentName = NormalizeLatexName(environmentName);
        if (string.IsNullOrWhiteSpace(environmentName))
            return LatexPackage.Unknown;

        if (ContainsName(AmsMathEnvironments, environmentName))
            return LatexPackage.AmsMath;
        if (ContainsName(AmsSymbEnvironments, environmentName))
            return LatexPackage.AmsSymb;

        return LatexPackage.Unknown;
    }

    internal static bool IsTabularLikeEnvironment(string environmentName)
        => environmentName is "matrix" or "matrix*" or "pmatrix" or "pmatrix*" or "bmatrix" or "bmatrix*" or "Bmatrix" or "Bmatrix*"
            or "vmatrix" or "vmatrix*" or "Vmatrix" or "Vmatrix*" or "smallmatrix" or "cases" or "array" or "align" or "align*"
            or "aligned" or "alignedat" or "alignat" or "alignat*" or "flalign" or "flalign*" or "eqnarray" or "eqnarray*";

    private static string NormalizeLatexName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        string normalized = name.Trim();
        return normalized[0] == '\\'
            ? normalized[1..]
            : normalized;
    }

    private static bool ContainsName(HashSet<string> set, string name)
    {
        if (set.Contains(name))
            return true;

        if (name.EndsWith('*') && name.Length > 1)
            return set.Contains(name[..^1]);

        return false;
    }

    private static HashSet<string> BuildSet(string raw)
    {
        HashSet<string> set = new(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw))
            return set;

        var lines = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string entry = lines[i];
            if (entry.Length == 0 || entry.StartsWith("#", StringComparison.Ordinal))
                continue;
            set.Add(entry);
        }

        return set;
    }
}
