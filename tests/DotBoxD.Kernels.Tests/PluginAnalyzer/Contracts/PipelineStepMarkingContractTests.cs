using System.Reflection;
using DotBoxD.Abstractions;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;
using DotBoxD.Plugins.Runtime.Subscriptions;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

/// <summary>
/// Guards that the framework's own fluent pipeline surface is fully annotated with the public
/// <see cref="PipelineSurfaceAttribute"/> / <see cref="PipelineStepAttribute"/> vocabulary, so the source
/// generator recognizes pipeline roles by attribute rather than by hardcoded method names. A new pipeline
/// type or a new stage/terminal overload that forgets the attribute fails here instead of silently falling
/// back to name-based recognition.
/// </summary>
public sealed class PipelineStepMarkingContractTests
{
    private static readonly IReadOnlyDictionary<string, PipelineStepRole> RecognizedMethodRoles =
        new Dictionary<string, PipelineStepRole>(StringComparer.Ordinal)
        {
            ["On"] = PipelineStepRole.Seed,
            ["Where"] = PipelineStepRole.Filter,
            ["Select"] = PipelineStepRole.Projection,
            ["Run"] = PipelineStepRole.Run,
            ["RunLocal"] = PipelineStepRole.RunLocal,
            ["Register"] = PipelineStepRole.Register,
            ["RegisterLocal"] = PipelineStepRole.RegisterLocal,
        };

    private static readonly (Type Type, PipelineTransport Transport)[] Surfaces =
    {
        (typeof(HookPipeline<,>), PipelineTransport.Local),
        (typeof(HookStage<,,>), PipelineTransport.Local),
        (typeof(SubscriptionPipeline<,>), PipelineTransport.Local),
        (typeof(SubscriptionStage<,,>), PipelineTransport.Local),
        (typeof(RemoteHookPipeline<>), PipelineTransport.Remote),
        (typeof(RemoteHookPipeline<,>), PipelineTransport.Remote),
        (typeof(RemoteHookStage<,>), PipelineTransport.Remote),
        (typeof(RemoteHookStage<,,>), PipelineTransport.Remote),
        (typeof(RemoteSubscriptionPipeline<>), PipelineTransport.Remote),
        (typeof(RemoteSubscriptionPipeline<,>), PipelineTransport.Remote),
        (typeof(RemoteSubscriptionStage<,>), PipelineTransport.Remote),
        (typeof(RemoteSubscriptionStage<,,>), PipelineTransport.Remote),
    };

    private static readonly Type[] Registries =
    {
        typeof(HookRegistry),
        typeof(RemoteHookRegistry),
        typeof(SubscriptionRegistry),
        typeof(RemoteSubscriptionRegistry),
    };

    public static TheoryData<Type, PipelineTransport> SurfaceTypes()
    {
        var data = new TheoryData<Type, PipelineTransport>();
        foreach (var (type, transport) in Surfaces)
        {
            data.Add(type, transport);
        }

        return data;
    }

    public static TheoryData<Type> RoleBearingTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var (type, _) in Surfaces)
        {
            data.Add(type);
        }

        // Registries are chain entry points (not surfaces): they carry the [PipelineStep(Seed)] On methods.
        foreach (var registry in Registries)
        {
            data.Add(registry);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SurfaceTypes))]
    public void Pipeline_surface_type_is_marked_with_expected_transport(Type type, PipelineTransport transport)
    {
        var surface = type.GetCustomAttribute<PipelineSurfaceAttribute>(inherit: false);

        Assert.True(surface is not null, $"{type.Name} is missing [PipelineSurface].");
        Assert.Equal(transport, surface!.Transport);
    }

    [Theory]
    [MemberData(nameof(RoleBearingTypes))]
    public void Recognized_pipeline_methods_are_marked_with_matching_role(Type type)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            if (!RecognizedMethodRoles.TryGetValue(method.Name, out var expectedRole))
            {
                continue;
            }

            var step = method.GetCustomAttribute<PipelineStepAttribute>(inherit: false);
            Assert.True(step is not null, $"{type.Name}.{method.Name} is missing [PipelineStep({expectedRole})].");
            Assert.Equal(expectedRole, step!.Role);
        }
    }

    [Fact]
    public void Registries_are_not_marked_as_surfaces()
    {
        foreach (var registry in new[]
                 {
                     typeof(HookRegistry), typeof(RemoteHookRegistry),
                     typeof(SubscriptionRegistry), typeof(RemoteSubscriptionRegistry),
                 })
        {
            Assert.Null(registry.GetCustomAttribute<PipelineSurfaceAttribute>(inherit: false));
        }
    }
}
