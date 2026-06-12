using BenchmarkDotNet.Running;
using System.Globalization;
using SafeIR.Benchmarks.Ipc;

if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase)) {
    await IpcAllocationSmoke.RunAsync();
    return;
}

var profileIndex = Array.FindIndex(args, arg => arg.Equals("--profile-ipc", StringComparison.OrdinalIgnoreCase));
if (profileIndex >= 0) {
    var transport = args.ElementAtOrDefault(profileIndex + 1) ?? IpcAllocationProfile.NamedPipeTransport;
    var iterationsText = args.ElementAtOrDefault(profileIndex + 2) ?? "10000";
    var iterations = int.Parse(iterationsText, CultureInfo.InvariantCulture);
    var disableTimeout = args.Contains("--no-timeout", StringComparer.OrdinalIgnoreCase);
    await IpcAllocationProfile.RunAsync(transport, iterations, disableTimeout);
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
