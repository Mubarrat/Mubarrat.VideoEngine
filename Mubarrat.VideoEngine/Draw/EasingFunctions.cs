namespace Mubarrat.VideoEngine.Draw;

public static class EasingFunctions
{
    public static readonly EasingFunction Linear = t => t;

    // Quadratic
    public static readonly EasingFunction QuadraticEaseIn = t => t * t;
    public static readonly EasingFunction QuadraticEaseOut = t => t * (2 - t);
    public static readonly EasingFunction QuadraticEaseInOut = t =>
        t < 0.5 ? 2 * t * t : -1 + (4 - 2 * t) * t;

    // Cubic
    public static readonly EasingFunction CubicEaseIn = t => t * t * t;
    public static readonly EasingFunction CubicEaseOut = t =>
    {
        double p = t - 1;
        return p * p * p + 1;
    };
    public static readonly EasingFunction CubicEaseInOut = t =>
        t < 0.5 ? 4 * t * t * t : (t - 1) * (2 * t - 2) * (2 * t - 2) + 1;

    // Quartic
    public static readonly EasingFunction QuarticEaseIn = t => t * t * t * t;
    public static readonly EasingFunction QuarticEaseOut = t =>
    {
        double p = t - 1;
        return 1 - p * p * p * p;
    };
    public static readonly EasingFunction QuarticEaseInOut = t =>
        t < 0.5 ? 8 * t * t * t * t : 1 - 8 * (t - 1) * (t - 1) * (t - 1) * (t - 1);

    // Quintic
    public static readonly EasingFunction QuinticEaseIn = t => t * t * t * t * t;
    public static readonly EasingFunction QuinticEaseOut = t =>
    {
        double p = t - 1;
        return p * p * p * p * p + 1;
    };
    public static readonly EasingFunction QuinticEaseInOut = t =>
        t < 0.5 ? 16 * t * t * t * t * t : 1 + 16 * (t - 1) * (t - 1) * (t - 1) * (t - 1) * (t - 1);

    // Sine
    public static readonly EasingFunction SineEaseIn = t => 1 - Math.Cos(t * Math.PI / 2);
    public static readonly EasingFunction SineEaseOut = t => Math.Sin(t * Math.PI / 2);
    public static readonly EasingFunction SineEaseInOut = t => -(Math.Cos(Math.PI * t) - 1) / 2;

    // Exponential
    public static readonly EasingFunction ExpoEaseIn = t => Math.Pow(2, 10 * (t - 1));
    public static readonly EasingFunction ExpoEaseOut = t => 1 - Math.Pow(2, -10 * t);
    public static readonly EasingFunction ExpoEaseInOut = t =>
        t < 0.5 ? Math.Pow(2, 20 * t - 10) / 2 : (2 - Math.Pow(2, -20 * t + 10)) / 2;

    // Bounce (simple approximation)
    public static readonly EasingFunction BounceEaseOut = t =>
    {
        if (t < 1 / 2.75) return 7.5625 * t * t;
        if (t < 2 / 2.75)
        {
            t -= 1.5 / 2.75;
            return 7.5625 * t * t + 0.75;
        }
        if (t < 2.5 / 2.75)
        {
            t -= 2.25 / 2.75;
            return 7.5625 * t * t + 0.9375;
        }
        t -= 2.625 / 2.75;
        return 7.5625 * t * t + 0.984375;
    };

    public static readonly EasingFunction BounceEaseIn = t => 1 - BounceEaseOut(1 - t);
    public static readonly EasingFunction BounceEaseInOut = t =>
        t < 0.5 ? BounceEaseIn(t * 2) * 0.5 : BounceEaseOut(t * 2 - 1) * 0.5 + 0.5;

    // Elastic (overshoot/back)
    public static readonly EasingFunction ElasticEaseOut = t =>
    {
        const double c4 = (Math.Tau) / 3;
        return t == 0 ? 0 : t == 1 ? 1 : Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * c4) + 1;
    };
    public static readonly EasingFunction ElasticEaseIn = t =>
    {
        const double c4 = (Math.Tau) / 3;
        return t == 0 ? 0 : t == 1 ? 1 : -Math.Pow(2, 10 * (t - 1)) * Math.Sin((t * 10 - 10.75) * c4);
    };
    public static readonly EasingFunction ElasticEaseInOut = t =>
    {
        const double c5 = (Math.Tau) / 4.5;
        return t == 0 ? 0 : t == 1 ? 1 :
            t < 0.5
                ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * c5)) / 2
                : (Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * c5)) / 2 + 1;
    };
}
