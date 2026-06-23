using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

internal sealed record AggroSample(
    string MonsterId,
    string Message,
    int MonsterLevel,
    int PlayerLevel,
    int Distance);

internal sealed class AggroAdapter : IPluginEventAdapter<AggroSample>
{
    public string EventName => "AggroEvent";

    public IReadOnlyList<Parameter> Parameters { get; } =
    [
        new("e_MonsterId", SandboxType.String),
        new("e_Message", SandboxType.String),
        new("e_MonsterLevel", SandboxType.I32),
        new("e_PlayerLevel", SandboxType.I32),
        new("e_Distance", SandboxType.I32)
    ];

    public IReadOnlyList<SandboxValue> ToSandboxValues(AggroSample e)
        =>
        [
            SandboxValue.FromString(e.MonsterId),
            SandboxValue.FromString(e.Message),
            SandboxValue.FromInt32(e.MonsterLevel),
            SandboxValue.FromInt32(e.PlayerLevel),
            SandboxValue.FromInt32(e.Distance)
        ];
}

internal sealed record ProbeSample(string TargetId, string Message, int Threshold);

internal sealed class ProbeAdapter : IPluginEventAdapter<ProbeSample>
{
    public string EventName => "ProbeEvent";

    public IReadOnlyList<Parameter> Parameters { get; } =
    [
        new("e_TargetId", SandboxType.String),
        new("e_Message", SandboxType.String),
        new("e_Threshold", SandboxType.I32)
    ];

    public IReadOnlyList<SandboxValue> ToSandboxValues(ProbeSample e)
        =>
        [
            SandboxValue.FromString(e.TargetId),
            SandboxValue.FromString(e.Message),
            SandboxValue.FromInt32(e.Threshold)
        ];
}
