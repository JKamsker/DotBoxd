using System.Diagnostics;
using System.Reflection;

namespace DotBoxD.Kernels.Benchmarks.File;

internal static class SafeFilePathSafetyProbe
{
    private const int Warmup = 1_000;
    private const int Iterations = 50_000;
    private const string RelativePath = "tenant/env/region/service/config/settings.json";
    private static readonly Action<string, string> EnsureNoReparsePoint = CreateEnsureNoReparsePointDelegate();

    public static void Run()
    {
        using var workspace = TempWorkspace.Create();
        _ = Measure(Warmup, "path safety guard x1", workspace, guardCalls: 1);
        _ = Measure(Warmup, "path safety guard x2", workspace, guardCalls: 2);

        var singleGuard = Measure(Iterations, "path safety guard x1", workspace, guardCalls: 1);
        var doubleGuard = Measure(Iterations, "path safety guard x2", workspace, guardCalls: 2);

        Console.WriteLine($"relative path = {RelativePath}");
        Console.WriteLine($"iterations = {Iterations:N0}");
        Write(singleGuard);
        Write(doubleGuard);
    }

    private static Measurement Measure(
        int iterations,
        string name,
        TempWorkspace workspace,
        int guardCalls)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            for (var guard = 0; guard < guardCalls; guard++)
            {
                EnsureNoReparsePoint(workspace.RootFull, workspace.FullPath);
            }
        }

        sw.Stop();
        return new Measurement(
            name,
            sw.Elapsed.TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    private static Action<string, string> CreateEnsureNoReparsePointDelegate()
    {
        var method = typeof(DotBoxD.Kernels.Runtime.Bindings.SafeFileSystem)
            .GetMethod(
                "EnsureNoReparsePoint",
                BindingFlags.NonPublic | BindingFlags.Static,
                [typeof(string), typeof(string)])!;
        return method.CreateDelegate<Action<string, string>>();
    }

    private static void Write(Measurement measurement)
        => Console.WriteLine(
            $"{measurement.Name,-22} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.Milliseconds * 1_000_000 / Iterations,9:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.AllocatedBytes / (double)Iterations,8:N1} B/op");

    private sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string rootPath)
        {
            RootPath = rootPath;
            RootFull = EnsureTrailingSeparator(Path.GetFullPath(rootPath));
            FullPath = Path.GetFullPath(Path.Combine(rootPath, RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            Directory.CreateDirectory(Path.GetDirectoryName(FullPath)!);
            System.IO.File.WriteAllText(FullPath, "tenant-settings");
        }

        public string RootPath { get; }

        public string RootFull { get; }

        public string FullPath { get; }

        public static TempWorkspace Create()
            => new(Path.Combine(Path.GetTempPath(), "dotboxd-safe-file-probe-" + Guid.NewGuid().ToString("N")));

        public void Dispose()
        {
            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string EnsureTrailingSeparator(string path)
            => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        long AllocatedBytes);
}
