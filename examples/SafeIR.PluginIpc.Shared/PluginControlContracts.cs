namespace SafeIR.PluginIpc.Shared;

using MessagePack;
using ShaRPC.Core.Attributes;

[ShaRpcService]
public interface IPluginControlService
{
    Task<IReadOnlyList<LiveSettingSnapshot>> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task SetSettingAsync(string name, string value, CancellationToken cancellationToken = default);

    Task ModifySettingsAsync(
        IReadOnlyList<LiveSettingUpdate> settings,
        bool atomic = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> PublishDamageAsync(
        DamageEventRequest request,
        CancellationToken cancellationToken = default);
}

[MessagePackObject]
public sealed class DamageEventRequest
{
    [Key(0)]
    public string DamageType { get; set; } = string.Empty;

    [Key(1)]
    public int Amount { get; set; }

    [Key(2)]
    public string TargetId { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class LiveSettingUpdate
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;

    [Key(1)]
    public string Value { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class LiveSettingSnapshot
{
    [Key(0)]
    public string Name { get; set; } = string.Empty;

    [Key(1)]
    public string Type { get; set; } = string.Empty;

    [Key(2)]
    public string Value { get; set; } = string.Empty;
}
