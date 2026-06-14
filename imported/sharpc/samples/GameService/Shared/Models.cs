using MessagePack;

namespace Shared;

[MessagePackObject]
public class PlayerId
{
    [Key(0)]
    public string Id { get; set; } = string.Empty;
}

[MessagePackObject]
public class PlayerState
{
    [Key(0)]
    public string PlayerId { get; set; } = string.Empty;

    [Key(1)]
    public string Name { get; set; } = string.Empty;

    [Key(2)]
    public int Level { get; set; }

    [Key(3)]
    public int Health { get; set; }

    [Key(4)]
    public int MaxHealth { get; set; }

    [Key(5)]
    public float PositionX { get; set; }

    [Key(6)]
    public float PositionY { get; set; }

    [Key(7)]
    public float PositionZ { get; set; }
}

[MessagePackObject]
public struct MoveRequest
{
    public MoveRequest()
    {
    }

    [Key(0)]
    public string? PlayerId { get; set; } = string.Empty;

    [Key(1)]
    public float X { get; set; }

    [Key(2)]
    public float Y { get; set; }

    [Key(3)]
    public float Z { get; set; }
}

[MessagePackObject]
public class ActionRequest
{
    [Key(0)]
    public string PlayerId { get; set; } = string.Empty;

    [Key(1)]
    public string ActionType { get; set; } = string.Empty;

    [Key(2)]
    public string? TargetId { get; set; }
}

[MessagePackObject]
public struct ActionResult
{
    [Key(0)]
    public bool Success { get; set; }

    [Key(1)]
    public string? Message { get; set; }
}

[MessagePackObject]
public class ServerStatus
{
    [Key(0)]
    public int PlayerCount { get; set; }

    [Key(1)]
    public string ServerTime { get; set; } = string.Empty;

    [Key(2)]
    public string Version { get; set; } = string.Empty;
}
