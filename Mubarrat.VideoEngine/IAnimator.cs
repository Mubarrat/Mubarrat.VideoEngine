namespace Mubarrat.VideoEngine;

public interface IAnimator<T>
{
    T Animate(in T from, in T to, double t);
}
