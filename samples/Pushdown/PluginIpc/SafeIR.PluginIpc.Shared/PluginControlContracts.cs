namespace SafeIR.PluginIpc.Shared;

using MessagePack;
using ShaRPC.Core.Attributes;

[ShaRpcService]
public interface IPluginControlService
{
    ValueTask<LiveSettingSnapshot[]> GetSettingsAsync(CancellationToken cancellationToken = default);

    ValueTask SetSettingAsync(string name, string value, CancellationToken cancellationToken = default);

    ValueTask ModifySettingsAsync(
        LiveSettingUpdate[] settings,
        bool atomic = false,
        CancellationToken cancellationToken = default);

    ValueTask<string[]> PublishDamageAsync(
        DamageEventRequest request,
        CancellationToken cancellationToken = default);
}

[MessagePackObject]
public readonly struct DamageEventRequest
{
    [SerializationConstructor]
    public DamageEventRequest(string damageType, int amount, string targetId)
    {
        DamageType = damageType;
        Amount = amount;
        TargetId = targetId;
    }

    [Key(0)]
    public string DamageType { get; }

    [Key(1)]
    public int Amount { get; }

    [Key(2)]
    public string TargetId { get; }
}

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
public readonly struct LiveSettingSnapshot
{
    [SerializationConstructor]
    public LiveSettingSnapshot(string name, string type, string value)
    {
        Name = name;
        Type = type;
        Value = value;
    }

    [Key(0)]
    public string Name { get; }

    [Key(1)]
    public string Type { get; }

    [Key(2)]
    public string Value { get; }
}
