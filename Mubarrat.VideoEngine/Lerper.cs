namespace Mubarrat.VideoEngine;

public class Lerper<T> : IAnimator<T> where T : ILerpable<T>
{
    public static Lerper<T> Instance => field ??= new();

    public T Animate(in T from, in T to, double t) => from.Lerp(to, t);
}
