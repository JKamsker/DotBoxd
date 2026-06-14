using MessagePack;

namespace Inventory.Shared;

[MessagePackObject]
public class Player
{
    [Key(0)] public string Id { get; set; } = string.Empty;
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public int Gold { get; set; }
}

[MessagePackObject]
public class Item
{
    [Key(0)] public string Id { get; set; } = string.Empty;
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public int UnitValue { get; set; }
    [Key(3)] public int Quantity { get; set; }
}
