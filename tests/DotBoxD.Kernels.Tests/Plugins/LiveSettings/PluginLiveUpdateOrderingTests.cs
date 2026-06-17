using System.Reflection;
using DotBoxD.Abstractions;
using DotBoxD.Kernels.PluginIpc.Server.Abstractions;
using DotBoxD.Kernels.PluginLocal;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Lifecycle;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class PluginLiveUpdateOrderingTests
{
    [Fact]
    public async Task AsyncSet_flush_keeps_newer_typed_value_after_older_pending_update()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        var kernel = server.Kernels.Get<FireDamageKernel>("fire-damage");
        var releaseStaleUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        kernel.UpdateMode = LiveUpdateMode.AsyncSet;
        PendingQueue(kernel.Kernel).Enqueue(() =>
        {
            releaseStaleUpdate.Task.GetAwaiter().GetResult();
            kernel.Kernel.Value.Set("MinDamage", 250);
        });

        kernel.Value.MinDamage = 300;
        var flush = kernel.FlushUpdatesAsync().AsTask();
        releaseStaleUpdate.SetResult();

        await flush.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(300, kernel.Kernel.Value.Get<int>("MinDamage"));
    }

    [Fact]
    public async Task Class_modify_keeps_batch_after_concurrent_publish_syncs_old_state()
    {
        var server = PluginAddendumTestPolicies.CreateServer();
        await server.InstallAsync(FireDamagePluginPackage.Create());
        var kernel = server.Kernels.Get<BlockingFireDamageSettings>("fire-damage");
        var definition = Assert.Single(kernel.Kernel.Value.Definitions, s => s.Name == "MinDamage");
        var setting = new OneShotBlockingLiveSetting(definition, 100);
        ReplaceSetting(kernel.Kernel.Value, setting);

        var modify = Task.Run(async () => await kernel.ModifyAsync(state =>
        {
            state.DamageType = "ice";
            state.MinDamage = 250;
        }).AsTask());
        try
        {
            await setting.Started.WaitAsync(TimeSpan.FromSeconds(5));

            var livePropertyReads = 0;
            kernel.Value.OnGetLiveProperty = _ => Interlocked.Increment(ref livePropertyReads);

            var buildInputStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var buildInput = Task.Run(() =>
            {
                buildInputStarted.SetResult();
                _ = BuildInput(kernel.Kernel, new DamageEvent("fire", 120, "player-1"));
            });
            await buildInputStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForLivePropertyReadsAsync(() => Volatile.Read(ref livePropertyReads));
            setting.Release();

            await Task.WhenAll(modify, buildInput).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            kernel.Value.OnGetLiveProperty = null;
            setting.Release();
        }

        Assert.Equal("ice", kernel.Kernel.Value.Get<string>("DamageType"));
        Assert.Equal(250, kernel.Kernel.Value.Get<int>("MinDamage"));
        Assert.Equal("ice", kernel.Value.DamageType);
        Assert.Equal(250, kernel.Value.MinDamage);
    }

    private static SandboxValue BuildInput(InstalledKernel kernel, DamageEvent e)
    {
        var method = typeof(InstalledKernel).GetMethod(
            "BuildInput",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        try
        {
            return (SandboxValue)method
                .MakeGenericMethod(typeof(DamageEvent))
                .Invoke(kernel, [DamageEventAdapter.Instance, e, kernel.Package.Entrypoints.ShouldHandle])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static async Task WaitForLivePropertyReadsAsync(Func<int> readCount)
    {
        for (var i = 0; i < 20 && readCount() < 2; i++)
        {
            await Task.Delay(10);
        }
    }

    private static PendingLiveUpdateQueue PendingQueue(InstalledKernel kernel)
    {
        var field = typeof(InstalledKernel).GetField(
            "_pendingLiveUpdates",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (PendingLiveUpdateQueue)field.GetValue(kernel)!;
    }

    private static void ReplaceSetting(LiveSettingStore store, ILiveSetting setting)
    {
        var field = typeof(LiveSettingStore).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var settings = (Dictionary<string, ILiveSetting>)field.GetValue(store)!;
        settings[setting.Name] = setting;
    }

    private sealed class BlockingFireDamageSettings
    {
        private string _damageType = "fire";
        private int _minDamage = 100;

        public Action<string>? OnGetLiveProperty { get; set; }

        [LiveSetting]
        public string DamageType
        {
            get
            {
                OnGetLiveProperty?.Invoke(nameof(DamageType));
                return _damageType;
            }
            set => _damageType = value;
        }

        [LiveSetting]
        public int MinDamage
        {
            get
            {
                OnGetLiveProperty?.Invoke(nameof(MinDamage));
                return _minDamage;
            }
            set => _minDamage = value;
        }
    }

    private sealed class OneShotBlockingLiveSetting(
        LiveSettingDefinition definition,
        object? initialValue) : ILiveSetting
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly object _gate = new();
        private object? _value = initialValue;
        private bool _blockNextSet = true;

        public Task Started => _started.Task;
        public string Name => definition.Name;
        public LiveSettingDefinition Definition => definition;

        public object? CurrentValue
        {
            get
            {
                lock (_gate)
                {
                    return _value;
                }
            }
        }

        public SandboxValue ToSandboxValue()
            => SandboxValue.FromInt32((int)CurrentValue!);

        public void SetObject(object? value)
        {
            if (ShouldBlock())
            {
                _started.SetResult();
                _release.Task.GetAwaiter().GetResult();
            }

            lock (_gate)
            {
                _value = value;
            }
        }

        public void Release()
            => _release.TrySetResult();

        private bool ShouldBlock()
        {
            lock (_gate)
            {
                if (!_blockNextSet)
                {
                    return false;
                }

                _blockNextSet = false;
                return true;
            }
        }
    }
}
