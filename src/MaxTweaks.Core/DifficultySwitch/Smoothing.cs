using System;

namespace MaxTweaks.Core.DifficultySwitch;

/// <summary>Frame-rate independent exponential approach, used for the highlight slide animation.</summary>
public static class Smoothing
{
    /// <summary>
    /// Moves <paramref name="current"/> towards <paramref name="target"/>, closing the fraction
    /// 1 - e^(-rate*dt) of the distance; snaps when within <paramref name="snap"/>.
    /// </summary>
    public static float Approach(float current, float target, float deltaTime, float rate, float snap = 0.01f)
    {
        if (float.IsNaN(current) || !(deltaTime > 0f) || !(rate > 0f))
        {
            return float.IsNaN(current) ? target : current;
        }

        var next = current + ((target - current) * (1f - (float)Math.Exp(-rate * deltaTime)));
        return Math.Abs(target - next) <= snap ? target : next;
    }
}
