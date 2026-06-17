using System.Reflection;

namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class GamePluginRemoteSettingsTests
{
    [Fact]
    public async Task Remote_SetValuesAsync_sends_only_mutated_live_settings()
    {
        var plugin = Assembly.LoadFrom(GamePluginAssemblyPath());
        var abstractions = Assembly.LoadFrom(GameServerAbstractionsPath());
        var kernelType = plugin.GetType(
            "DotBoxD.Kernels.Game.Plugin.Kernels.GuardianKernel",
            throwOnError: true)!;
        var controlType = abstractions.GetType(
            "DotBoxD.Kernels.Game.Server.Abstractions.Ipc.IGamePluginControlService",
            throwOnError: true)!;
        var handleType = plugin.GetType(
            "DotBoxD.Kernels.Game.Plugin.Client.RemoteKernelHandle`1",
            throwOnError: true)!
            .MakeGenericType(kernelType);
        var control = DispatchProxy.Create(controlType, typeof(CapturingControlProxy));
        var capture = (CapturingControlProxy)control;
        var handle = Activator.CreateInstance(
            handleType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [control, "guardian"],
            culture: null)!;

        var setValues = handleType.GetMethod("SetValuesAsync")!;
        var updateAction = CreatePropertySetter(kernelType, "AggroRange", 6);
        var pending = (ValueTask)setValues.Invoke(handle, [updateAction, false])!;
        await pending;

        var update = Assert.Single(capture.Updates);
        Assert.Equal("AggroRange", update.Name);
        Assert.Equal("6", update.Value);
    }

    private static Delegate CreatePropertySetter(Type targetType, string propertyName, object value)
    {
        var delegateType = typeof(Action<>).MakeGenericType(targetType);
        var parameter = System.Linq.Expressions.Expression.Parameter(targetType, "target");
        var property = System.Linq.Expressions.Expression.Property(parameter, propertyName);
        var assignment = System.Linq.Expressions.Expression.Assign(
            property,
            System.Linq.Expressions.Expression.Constant(value, property.Type));
        return System.Linq.Expressions.Expression.Lambda(delegateType, assignment, parameter).Compile();
    }

    private static string GamePluginAssemblyPath()
        => SampleAssemblyPath(
            "DotBoxD.Kernels.Game.Plugin",
            "DotBoxD.Kernels.Game.Plugin.dll");

    private static string GameServerAbstractionsPath()
        => SampleAssemblyPath(
            "DotBoxD.Kernels.Game.Server.Abstractions",
            "DotBoxD.Kernels.Game.Server.Abstractions.dll");

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
            "Kernels",
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
            if (targetMethod?.Name == "UpdateSettingsAsync")
            {
                CaptureUpdates((Array)args![1]!);
                return ValueTask.CompletedTask;
            }

            throw new NotImplementedException(targetMethod?.Name);
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
}
