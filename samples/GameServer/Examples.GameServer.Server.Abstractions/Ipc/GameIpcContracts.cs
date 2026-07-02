using MessagePack;

namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc;

[MessagePackObject]
public readonly struct LiveSettingUpdate
{
    [SerializationConstructor]
    public LiveSettingUpdate(string name, string value)
    {
        Name = name;
        Value = value;
    }

    [Key(0)]
    public string Name { get; }

    [Key(1)]
    public string Value { get; }
}

[MessagePackObject]
public readonly struct EntitySnapshot
{
    [SerializationConstructor]
    public EntitySnapshot(string id, string kind, int level, int hp, int position, int gold = 0)
    {
        Id = id;
        Kind = kind;
        Level = level;
        Hp = hp;
        Position = position;
        Gold = gold;
    }

    [Key(0)]
    public string Id { get; }

    [Key(1)]
    public string Kind { get; }

    [Key(2)]
    public int Level { get; }

    [Key(3)]
    public int Hp { get; }

    [Key(4)]
    public int Position { get; }

    [Key(5)]
    public int Gold { get; }
}

[MessagePackObject]
public readonly struct WorldSnapshot
{
    [SerializationConstructor]
    public WorldSnapshot(EntitySnapshot[] entities, int tick)
    {
        Entities = entities;
        Tick = tick;
    }

    [Key(0)]
    public EntitySnapshot[] Entities { get; }

    [Key(1)]
    public int Tick { get; }
}
