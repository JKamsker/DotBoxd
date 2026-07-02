using System.Linq.Expressions;
using System.Reflection;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class GamePluginRemoteSettingsTests
{
    [Fact]
    public async Task Remote_Set_apply_sends_only_selected_live_settings()
    {
        var plugin = Assembly.LoadFrom(GamePluginAssemblyPath());
        var abstractions = Assembly.LoadFrom(GameServerAbstractionsPath());
        var kernelType = plugin.GetType(
            "DotBoxD.Kernels.Game.Plugin.Kernels.GuardianKernel",
            throwOnError: true)!;
        var controlType = abstractions.GetType(
            "DotBoxD.Kernels.Game.Server.Abstractions.Ipc.IGamePluginControlService",
            throwOnError: true)!;
        var worldType = abstractions.GetType(
            "DotBoxD.Kernels.Game.Server.Abstractions.IGameWorldAccess",
            throwOnError: true)!;
        var serverType = plugin.GetType(
            "DotBoxD.Kernels.Game.Plugin.GamePluginServer",
            throwOnError: true)!;
        var builderType = plugin.GetType(
            "DotBoxD.Kernels.Game.Plugin.GamePluginServerBuilder",
            throwOnError: true)!;
        var serviceType = abstractions.GetType(
            "DotBoxD.Kernels.Game.Server.Abstractions.IMonsterAggroService",
            throwOnError: true)!;
        var control = DispatchProxy.Create(controlType, typeof(CapturingControlProxy));
        var world = DispatchProxy.Create(worldType, typeof(NoopProxy));
        var capture = (CapturingControlProxy)control;
        var server = await BuildStartedServer(
            builderType,
            control,
            world,
            serviceType,
            kernelType);
        var getHandle = serverType
            .GetMethod("Get", BindingFlags.Instance | BindingFlags.Public)!
            .MakeGenericMethod(kernelType);
        var handle = getHandle.Invoke(server, [])!;
        var handleType = handle.GetType();

        var set = handleType.GetMethods()
            .Single(method => method.Name == "Set" && method.IsGenericMethodDefinition)
            .MakeGenericMethod(typeof(int));
        var apply = handleType.GetMethod("ApplyAsync")!;
        var selector = CreatePropertySelector(kernelType, "AggroRange");
        set.Invoke(handle, [selector, 6]);
        var pending = (ValueTask)apply.Invoke(handle, [false])!;
        await pending;

        var update = Assert.Single(capture.Updates);
        Assert.Equal("AggroRange", update.Name);
        Assert.Equal("6", update.Value);
    }

    private static async Task<object> BuildStartedServer(
        Type builderType,
        object control,
        object world,
        Type serviceType,
        Type kernelType)
    {
        var builder = builderType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "FromConnection" && method.GetParameters().Length == 2)
            .Invoke(null, [control, world])!;
        var setup = builder.GetType().GetMethod("Setup", BindingFlags.Public | BindingFlags.Instance)!;
        var setupActionType = setup.GetParameters()[0].ParameterType;
        var setupType = setupActionType.GetGenericArguments()[0];
        var configure = CreateSetupDelegate(setupActionType, setupType, serviceType, kernelType);
        setup.Invoke(builder, [configure]);

        var server = builder.GetType().GetMethod("Build", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(builder, null)!;
        var start = server.GetType().GetMethod("StartAsync", BindingFlags.Public | BindingFlags.Instance)!;
        await AwaitValueTask(start.Invoke(server, [CancellationToken.None])!);
        return server;
    }

    private static Delegate CreateSetupDelegate(
        Type setupActionType,
        Type setupType,
        Type serviceType,
        Type kernelType)
        => Delegate.CreateDelegate(
            setupActionType,
            typeof(GamePluginRemoteSettingsTests)
                .GetMethod(nameof(ConfigureGuardian), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(setupType, serviceType, kernelType));

    private static void ConfigureGuardian<TSetup, TService, TKernel>(TSetup setup)
    {
        setup!.GetType().GetMethod("Replace")!
            .MakeGenericMethod(typeof(TService), typeof(TKernel))
            .Invoke(setup, null);
    }

    private static async Task AwaitValueTask(object valueTask)
    {
        var asTask = valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!;
        await (Task)asTask.Invoke(valueTask, null)!;
    }

    private static LambdaExpression CreatePropertySelector(Type targetType, string propertyName)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(targetType, "target");
        var property = System.Linq.Expressions.Expression.Property(parameter, propertyName);
        var delegateType = typeof(Func<,>).MakeGenericType(targetType, property.Type);
        return System.Linq.Expressions.Expression.Lambda(delegateType, property, parameter);
    }

    private static string GamePluginAssemblyPath()
        => SampleAssemblyPath(
            "Examples.GameServer.Plugin",
            "Examples.GameServer.Plugin.dll");

    private static string GameServerAbstractionsPath()
        => SampleAssemblyPath(
            "Examples.GameServer.Server.Abstractions",
            "Examples.GameServer.Server.Abstractions.dll");

    private static string SampleAssemblyPath(string projectName, string assemblyName)
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        var configuration = output.Parent!.Name;
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "samples",
            "GameServer",
            projectName,
            "bin",
            configuration,
            "net10.0",
            assemblyName));
    }

    private class CapturingControlProxy : DispatchProxy
    {
        public List<(string Name, string Value)> Updates { get; } = [];

        protected override object Invoke(
            MethodInfo? targetMethod,
            object?[]? args)
        {
            if (targetMethod?.Name == "InstallPluginAsync")
            {
                return ValueTask.FromResult("guardian");
            }

            if (targetMethod?.Name == "UpdateSettingsAsync")
            {
                CaptureUpdates((Array)args![1]!);
                return ValueTask.CompletedTask;
            }

            throw new NotSupportedException(targetMethod?.Name);
        }

        private void CaptureUpdates(Array updates)
        {
            foreach (var update in updates)
            {
                var type = update!.GetType();
                Updates.Add((
                    (string)type.GetProperty("Name")!.GetValue(update)!,
                    (string)type.GetProperty("Value")!.GetValue(update)!));
            }
        }
    }

    private class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args)
        {
            if (targetMethod?.Name.StartsWith("get_", StringComparison.Ordinal) == true &&
                targetMethod.ReturnType.IsInterface)
            {
                return DispatchProxy.Create(targetMethod.ReturnType, typeof(NoopProxy));
            }

            return targetMethod?.ReturnType.IsValueType == true
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }
}
