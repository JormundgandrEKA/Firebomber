using Godot;
using System;

namespace Firebomber;

/// <summary>Draws above the night: fire, flashes and tracer stay bright when the land goes dark.</summary>
public partial class Glow : Node2D
{
    public Main M = null!;
    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;
    public override void _Process(double delta) => QueueRedraw();
    public override void _Draw()
    {
        float d = (float)M.Air.Dark; if (d <= .02f) return;
        M.FireV.GlowTo(this, d); M.FView.TracersTo(this, d); M.FView.FlakTo(this, 1); M.View.GunFlashes(this); M.View.FieldLabels(this);
    }
}
