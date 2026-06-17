namespace DotBoxD.Kernels.Game.Server.Abstractions;

using System.Globalization;

/// <summary>
/// The plugin -&gt; server command DSL. A kernel's only sandbox capability is
/// <c>host.message.write</c>; the meaning of the message text is defined here in the example, not in
/// the DotBoxD.Kernels core. Two verbs are understood:
/// <list type="bullet">
///   <item><description><c>calm:&lt;playerId&gt;:&lt;strength&gt;</c> sent to a monster id — reduce
///   that monster's aggro toward the player.</description></item>
///   <item><description><c>taunt:&lt;targetId&gt;</c> sent to an attacker id — make the attacker
///   switch away from the target it is bullying.</description></item>
/// </list>
/// Both the host-side local preview and the server-side command sink share these helpers so the
/// wire format never drifts.
/// </summary>
public static class GameCommands
{
    public const string CalmVerb = "calm";
    public const string TauntVerb = "taunt";

    private const char Separator = ':';

    public static string FormatCalm(string playerId, string strength)
        => string.Concat(CalmVerb, ":", playerId, ":", strength);

    public static string FormatTaunt(string targetId)
        => string.Concat(TauntVerb, ":", targetId);

    /// <summary>
    /// Parses a raw plugin command. Returns <see langword="false"/> for any unknown verb or
    /// malformed payload so callers can ignore invalid commands without throwing.
    /// </summary>
    public static bool TryParse(string raw, out GameCommand command)
    {
        command = default;
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        var parts = raw.Split(Separator);
        return parts[0] switch
        {
            CalmVerb => TryParseCalm(parts, out command),
            TauntVerb => TryParseTaunt(parts, out command),
            _ => false
        };
    }

    private static bool TryParseCalm(string[] parts, out GameCommand command)
    {
        command = default;
        if (parts.Length != 3 || parts[1].Length == 0)
        {
            return false;
        }

        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var strength))
        {
            return false;
        }

        command = new GameCommand(CommandVerb.Calm, parts[1], strength);
        return true;
    }

    private static bool TryParseTaunt(string[] parts, out GameCommand command)
    {
        command = default;
        if (parts.Length != 2 || parts[1].Length == 0)
        {
            return false;
        }

        command = new GameCommand(CommandVerb.Taunt, parts[1], 0);
        return true;
    }
}

public enum CommandVerb
{
    Calm,
    Taunt
}

/// <summary>
/// A parsed plugin command. <see cref="Argument"/> is the player id (calm) or the original target id
/// (taunt); <see cref="Strength"/> is only meaningful for <see cref="CommandVerb.Calm"/>.
/// </summary>
public readonly record struct GameCommand(CommandVerb Verb, string Argument, int Strength);
