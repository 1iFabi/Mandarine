using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Mandarine;

/// <summary>
/// A spring in Apple's designer terms (WWDC 2018 "Designing Fluid Interfaces"): damping ratio
/// (1 = no overshoot) and response (seconds, not a duration). Retargeting keeps the current value
/// and velocity, so motion is always interruptible and never jumps.
/// </summary>
public sealed class Spring
{
    public double Value, Velocity, Target;
    public double DampingRatio = 1, Response = 0.3;
    public double Delay; // seconds before it starts moving (staggered entrances)

    public Spring(double value) { Value = Target = value; }

    public bool Settled => Delay <= 0 && Math.Abs(Value - Target) < 1e-3 && Math.Abs(Velocity) < 1e-3;

    public void Step(double dt)
    {
        if (Delay > 0) { Delay -= dt; return; }
        // Apple's mapping from (damping ratio, response) to stiffness and damping, mass = 1.
        var stiffness = Math.Pow(2 * Math.PI / Response, 2);
        var damping = 4 * Math.PI * DampingRatio / Response;
        Velocity += (-stiffness * (Value - Target) - damping * Velocity) * dt;
        Value += Velocity * dt;
    }

    public void Snap() { Value = Target; Velocity = 0; Delay = 0; }
}

/// <summary>Drives springs on WPF's display-synced frame clock; idles when everything has settled.</summary>
public sealed class SpringClock
{
    readonly List<(Spring Spring, Action<double> Apply)> _springs = new();
    TimeSpan _last;
    bool _running;

    /// <summary>When set (Windows animations off), springs jump to their targets instead of moving.</summary>
    public bool ReduceMotion { get; set; }

    public Spring Add(Spring spring, Action<double> apply)
    {
        _springs.Add((spring, apply));
        apply(spring.Value);
        return spring;
    }

    public void Clear() => _springs.Clear();

    public void Remove(Spring spring) => _springs.RemoveAll(s => s.Spring == spring);

    /// <summary>Call after changing any target.</summary>
    public void Kick()
    {
        if (_running) return;
        _running = true;
        _last = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = _last == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _last).TotalSeconds, 0, 1 / 20.0);
        if (now == _last) return; // WPF can raise Rendering twice per frame
        _last = now;

        var settled = true;
        foreach (var (spring, apply) in _springs.ToArray())
        {
            if (ReduceMotion) spring.Snap();
            // Fixed sub-steps keep stiff springs stable at any frame rate.
            for (var t = 0.0; t < dt; t += 1 / 480.0) spring.Step(Math.Min(1 / 480.0, dt - t));
            apply(spring.Value);
            settled &= spring.Settled;
        }
        if (!settled) return;
        CompositionTarget.Rendering -= OnFrame;
        _running = false;
        Settled?.Invoke();
    }

    public event Action? Settled;
}
