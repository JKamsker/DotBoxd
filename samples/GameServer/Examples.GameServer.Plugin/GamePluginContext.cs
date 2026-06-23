namespace DotBoxD.Kernels.Game.Plugin;

public sealed partial class GamePluginContext
{
    public string DamageDecisionReason => "remote";

    public string FormatCalmTarget(string monsterId) => "ctx:" + monsterId;

    public int ScaleDamageDecision(int damage) => damage * 2;
}
