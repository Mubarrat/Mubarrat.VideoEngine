using System.Reflection;
using System.Reflection.Emit;

namespace Mubarrat.VideoEngine.Path;

// ─────────────────────────────────────────────────────────────────────────────
// CurveJitCompiler — emits per-shape delegates via Reflection.Emit
// ─────────────────────────────────────────────────────────────────────────────
internal static class CurveJitCompiler
{
    // MethodInfo handles for KernelMath statics — resolved once at startup
    private static readonly MethodInfo _lineDistance = typeof(KernelMath).GetMethod(nameof(KernelMath.LineDistance), BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo _quadDistance = typeof(KernelMath).GetMethod(nameof(KernelMath.QuadDistance), BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo _cubicDistance = typeof(KernelMath).GetMethod(nameof(KernelMath.CubicDistance), BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo _arcDistance = typeof(KernelMath).GetMethod(nameof(KernelMath.ArcDistance), BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo _lineWinding = typeof(KernelMath).GetMethod(nameof(KernelMath.LineWinding), BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo _quadWinding = typeof(KernelMath).GetMethod(nameof(KernelMath.QuadWinding), BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo _cubicWinding = typeof(KernelMath).GetMethod(nameof(KernelMath.CubicWinding), BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo _arcWinding = typeof(KernelMath).GetMethod(nameof(KernelMath.ArcWinding), BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo _rectDist = typeof(KernelMath).GetMethod(nameof(KernelMath.RectDist), BindingFlags.Public | BindingFlags.Static)!;
    private static readonly MethodInfo _mathSqrt = typeof(Math).GetMethod(nameof(Math.Sqrt), [typeof(double)])!;
    private static readonly MethodInfo _mathMin_d = typeof(Math).GetMethod(nameof(Math.Min), [typeof(double), typeof(double)])!;

    // Shared dynamic assembly — all generated types live here
    private static readonly AssemblyBuilder _asm =
        AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("VideoEngine.Generated"),
            AssemblyBuilderAccess.Run | AssemblyBuilderAccess.RunAndCollect);
    private static readonly ModuleBuilder _mod = _asm.DefineDynamicModule("Generated");
    private static int _typeCounter;

    // ─── Kernel descriptor — all data needed to emit code for one segment ────

    private abstract record KernelDesc(Rect AABB);

    private record LineDesc(double AX, double AY, double BX, double BY, Rect AABB)
        : KernelDesc(AABB);

    private record QuadDesc(
        double P0X, double P0Y, double P1X, double P1Y, double P2X, double P2Y, Rect AABB)
        : KernelDesc(AABB);

    private record CubicDesc(
        double P0X, double P0Y, double P1X, double P1Y,
        double P2X, double P2Y, double P3X, double P3Y, Rect AABB)
        : KernelDesc(AABB);

    private record ArcDesc(
        double StartX, double StartY, double EndX, double EndY,
        double RX, double RY, double Rot, bool LargeArc, bool Sweep, Rect AABB)
        : KernelDesc(AABB);

    // ─── Public entry ────────────────────────────────────────────────────────

    public static CompiledShape Compile(IPathSegment[] segs, bool nonZero)
    {
        var descs = BuildDescs(segs);

        if (descs.Count == 0)
        {
            // Empty path — return trivial delegates
            return new CompiledShape(
                (_, _) => double.MaxValue,
                (_, _) => 0);
        }

        // Sort by AABB perimeter ascending so tightest kernels are checked first.
        // Tighter kernels will fail the AABB lower-bound check sooner on most pixels,
        // meaning the more expensive distance call is skipped earlier.
        descs.Sort((a, b) =>
        {
            double pa = 2 * (a.AABB.Width + a.AABB.Height);
            double pb = 2 * (b.AABB.Width + b.AABB.Height);
            return pa.CompareTo(pb);
        });

        int id = Interlocked.Increment(ref _typeCounter);
        var tb = _mod.DefineType($"Shape_{id}", TypeAttributes.Public | TypeAttributes.Sealed);

        var distMethod = EmitDistanceMethod(tb, descs);
        var windMethod = nonZero ? EmitWindingMethod(tb, descs) : EmitEvenOddMethod(tb, descs);

        var t = tb.CreateType()!;

        var dist = (Func<double, double, double>)Delegate.CreateDelegate(typeof(Func<double, double, double>), t.GetMethod(distMethod.Name)!);
        var wind = (Func<double, double, int>)Delegate.CreateDelegate(typeof(Func<double, double, int>), t.GetMethod(windMethod.Name)!);

        return new CompiledShape(dist, wind);
    }

    // ─── Descriptor builder ──────────────────────────────────────────────────

    private static List<KernelDesc> BuildDescs(IPathSegment[] segs)
    {
        var list = new List<KernelDesc>(segs.Length);
        var current = default(Point);
        bool has = false;

        foreach (var s in segs)
        {
            switch (s)
            {
                case MoveSegment m:
                    current = m.Point;
                    has = true;
                    break;

                case LineSegment l when has:
                    list.Add(new LineDesc(
                        current.X, current.Y, l.End.X, l.End.Y,
                        KernelMath.LineAABB(current.X, current.Y, l.End.X, l.End.Y)));
                    current = l.End;
                    break;

                case QuadraticSegment q when has:
                    list.Add(new QuadDesc(
                        current.X, current.Y,
                        q.Control.X, q.Control.Y,
                        q.End.X, q.End.Y,
                        KernelMath.QuadAABB(current.X, current.Y, q.Control.X, q.Control.Y, q.End.X, q.End.Y)));
                    current = q.End;
                    break;

                case CubicSegment c when has:
                    list.Add(new CubicDesc(
                        current.X, current.Y,
                        c.Control1.X, c.Control1.Y,
                        c.Control2.X, c.Control2.Y,
                        c.End.X, c.End.Y,
                        KernelMath.CubicAABB(current.X, current.Y, c.Control1.X, c.Control1.Y, c.Control2.X, c.Control2.Y, c.End.X, c.End.Y)));
                    current = c.End;
                    break;

                //case ArcSegment a when has:
                //    list.Add(new ArcDesc(
                //        current.X, current.Y,
                //        a.End.X, a.End.Y,
                //        a.RadiusX, a.RadiusY,
                //        a.XAxisRotation, a.LargeArcFlag, a.SweepFlag,
                //        KernelMath.ArcAABB(current.X, current.Y, a.End.X, a.End.Y,
                //            a.RadiusX, a.RadiusY, a.XAxisRotation, a.LargeArcFlag, a.SweepFlag)));
                //    current = a.End;
                //    break;
            }
        }

        return list;
    }

    // ─── IL emitters ─────────────────────────────────────────────────────────

    /// <summary>
    /// Emits:
    ///   public static double Distance(double px, double py)
    ///   {
    ///       double best = double.MaxValue;
    ///       // per kernel (sorted by AABB perimeter):
    ///       {
    ///           double kd = RectDist(px, py, rx, ry, rw, rh);
    ///           if (kd < best) {
    ///               double d = LineDistance(px, py, ax, ay, bx, by);
    ///               if (d < best) best = d;
    ///           }
    ///       }
    ///       ...
    ///       return best;
    ///   }
    /// </summary>
    private static MethodBuilder EmitDistanceMethod(TypeBuilder tb, List<KernelDesc> descs)
    {
        var mb = tb.DefineMethod("Distance",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(double), [typeof(double), typeof(double)]);
        var il = mb.GetILGenerator();

        var best = il.DeclareLocal(typeof(double));  // local 2
        var kd = il.DeclareLocal(typeof(double));  // local 3
        var d = il.DeclareLocal(typeof(double));  // local 4

        // best = double.MaxValue
        il.Emit(OpCodes.Ldc_R8, double.MaxValue);
        il.Emit(OpCodes.Stloc, best);

        foreach (var desc in descs)
        {
            var skipKernel = il.DefineLabel();
            var skipUpdate = il.DefineLabel();

            // kd = RectDist(px, py, aabb.X, aabb.Y, aabb.Width, aabb.Height)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_R8, desc.AABB.X);
            il.Emit(OpCodes.Ldc_R8, desc.AABB.Y);
            il.Emit(OpCodes.Ldc_R8, desc.AABB.Width);
            il.Emit(OpCodes.Ldc_R8, desc.AABB.Height);
            il.Emit(OpCodes.Call, _rectDist);
            il.Emit(OpCodes.Stloc, kd);

            // if (kd >= best) continue
            il.Emit(OpCodes.Ldloc, kd);
            il.Emit(OpCodes.Ldloc, best);
            il.Emit(OpCodes.Bge_Un, skipKernel);

            // d = KernelDistance(px, py, ...constants...)
            EmitDistanceCall(il, desc);
            il.Emit(OpCodes.Stloc, d);

            // if (d < best) best = d
            il.Emit(OpCodes.Ldloc, d);
            il.Emit(OpCodes.Ldloc, best);
            il.Emit(OpCodes.Bge_Un, skipUpdate);
            il.Emit(OpCodes.Ldloc, d);
            il.Emit(OpCodes.Stloc, best);
            il.MarkLabel(skipUpdate);

            il.MarkLabel(skipKernel);
        }

        il.Emit(OpCodes.Ldloc, best);
        il.Emit(OpCodes.Ret);
        return mb;
    }

    /// <summary>
    /// Emits:
    ///   public static int Winding(double px, double py)
    ///   {
    ///       int w = 0;
    ///       w += LineWinding(px, py, ...);
    ///       ...
    ///       return w;
    ///   }
    /// </summary>
    private static MethodBuilder EmitWindingMethod(TypeBuilder tb, List<KernelDesc> descs)
    {
        var mb = tb.DefineMethod("Winding",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int), [typeof(double), typeof(double)]);
        var il = mb.GetILGenerator();

        var w = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, w);

        foreach (var desc in descs)
        {
            il.Emit(OpCodes.Ldloc, w);
            EmitWindingCall(il, desc);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, w);
        }

        il.Emit(OpCodes.Ldloc, w);
        il.Emit(OpCodes.Ret);
        return mb;
    }

    /// <summary>
    /// Emits:
    ///   public static int EvenOdd(double px, double py)
    ///   {
    ///       int hits = 0;
    ///       hits += Math.Abs(LineWinding(px, py, ...));   // 0 or 1 per kernel
    ///       ...
    ///       return (hits & 1);
    ///   }
    /// </summary>
    private static MethodBuilder EmitEvenOddMethod(TypeBuilder tb, List<KernelDesc> descs)
    {
        var mb = tb.DefineMethod("EvenOdd",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int), [typeof(double), typeof(double)]);
        var il = mb.GetILGenerator();

        var hits = il.DeclareLocal(typeof(int));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, hits);

        var absMethod = typeof(Math).GetMethod(nameof(Math.Abs), [typeof(int)])!;

        foreach (var desc in descs)
        {
            // hits += Math.Abs(KernelWinding(px,py,...))   — gives 0 or 1 per crossing
            il.Emit(OpCodes.Ldloc, hits);
            EmitWindingCall(il, desc);
            il.Emit(OpCodes.Call, absMethod);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stloc, hits);
        }

        // return (hits & 1)
        il.Emit(OpCodes.Ldloc, hits);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.And);
        il.Emit(OpCodes.Ret);
        return mb;
    }

    // ─── Per-kernel distance call emitters ──────────────────────────────────

    private static void EmitDistanceCall(ILGenerator il, KernelDesc desc)
    {
        // All push: (px, py, ...constants...) then Call
        switch (desc)
        {
            case LineDesc l:
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_R8, l.AX); il.Emit(OpCodes.Ldc_R8, l.AY);
                il.Emit(OpCodes.Ldc_R8, l.BX); il.Emit(OpCodes.Ldc_R8, l.BY);
                il.Emit(OpCodes.Call, _lineDistance);
                break;

            case QuadDesc q:
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_R8, q.P0X); il.Emit(OpCodes.Ldc_R8, q.P0Y);
                il.Emit(OpCodes.Ldc_R8, q.P1X); il.Emit(OpCodes.Ldc_R8, q.P1Y);
                il.Emit(OpCodes.Ldc_R8, q.P2X); il.Emit(OpCodes.Ldc_R8, q.P2Y);
                il.Emit(OpCodes.Call, _quadDistance);
                break;

            case CubicDesc c:
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_R8, c.P0X); il.Emit(OpCodes.Ldc_R8, c.P0Y);
                il.Emit(OpCodes.Ldc_R8, c.P1X); il.Emit(OpCodes.Ldc_R8, c.P1Y);
                il.Emit(OpCodes.Ldc_R8, c.P2X); il.Emit(OpCodes.Ldc_R8, c.P2Y);
                il.Emit(OpCodes.Ldc_R8, c.P3X); il.Emit(OpCodes.Ldc_R8, c.P3Y);
                il.Emit(OpCodes.Call, _cubicDistance);
                break;

            case ArcDesc a:
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_R8, a.StartX); il.Emit(OpCodes.Ldc_R8, a.StartY);
                il.Emit(OpCodes.Ldc_R8, a.EndX); il.Emit(OpCodes.Ldc_R8, a.EndY);
                il.Emit(OpCodes.Ldc_R8, a.RX); il.Emit(OpCodes.Ldc_R8, a.RY);
                il.Emit(OpCodes.Ldc_R8, a.Rot);
                il.Emit(a.LargeArc ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                il.Emit(a.Sweep ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Call, _arcDistance);
                break;
        }
    }

    private static void EmitWindingCall(ILGenerator il, KernelDesc desc)
    {
        switch (desc)
        {
            case LineDesc l:
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_R8, l.AX); il.Emit(OpCodes.Ldc_R8, l.AY);
                il.Emit(OpCodes.Ldc_R8, l.BX); il.Emit(OpCodes.Ldc_R8, l.BY);
                il.Emit(OpCodes.Call, _lineWinding);
                break;

            case QuadDesc q:
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_R8, q.P0X); il.Emit(OpCodes.Ldc_R8, q.P0Y);
                il.Emit(OpCodes.Ldc_R8, q.P1X); il.Emit(OpCodes.Ldc_R8, q.P1Y);
                il.Emit(OpCodes.Ldc_R8, q.P2X); il.Emit(OpCodes.Ldc_R8, q.P2Y);
                il.Emit(OpCodes.Call, _quadWinding);
                break;

            case CubicDesc c:
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_R8, c.P0X); il.Emit(OpCodes.Ldc_R8, c.P0Y);
                il.Emit(OpCodes.Ldc_R8, c.P1X); il.Emit(OpCodes.Ldc_R8, c.P1Y);
                il.Emit(OpCodes.Ldc_R8, c.P2X); il.Emit(OpCodes.Ldc_R8, c.P2Y);
                il.Emit(OpCodes.Ldc_R8, c.P3X); il.Emit(OpCodes.Ldc_R8, c.P3Y);
                il.Emit(OpCodes.Call, _cubicWinding);
                break;

            case ArcDesc a:
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_R8, a.StartX); il.Emit(OpCodes.Ldc_R8, a.StartY);
                il.Emit(OpCodes.Ldc_R8, a.EndX); il.Emit(OpCodes.Ldc_R8, a.EndY);
                il.Emit(OpCodes.Ldc_R8, a.RX); il.Emit(OpCodes.Ldc_R8, a.RY);
                il.Emit(OpCodes.Ldc_R8, a.Rot);
                il.Emit(a.LargeArc ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                il.Emit(a.Sweep ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Call, _arcWinding);
                break;
        }
    }
}
