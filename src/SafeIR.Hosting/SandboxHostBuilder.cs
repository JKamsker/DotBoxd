namespace SafeIR.Hosting;

using SafeIR;
using SafeIR.Compiler;
using SafeIR.Interpreter;
using SafeIR.Runtime;
using SafeIR.Verifier;

public sealed class SandboxHostBuilder
{
    private readonly BindingRegistryBuilder _bindings = new();
    private ISandboxInterpreter? _interpreter;
    private ISandboxCompiler? _compiler;
    private string? _compilerCacheDirectory;
    private bool _useCompiler;

    public SandboxHostBuilder AddDefaultPureBindings()
    {
        _bindings.AddDefaultPureBindings();
        return this;
    }

    public SandboxHostBuilder AddFileBindings()
    {
        _bindings.AddFileBindings();
        return this;
    }

    public SandboxHostBuilder AddTimeBindings()
    {
        _bindings.AddTimeBindings();
        return this;
    }

    public SandboxHostBuilder AddRandomBindings()
    {
        _bindings.AddRandomBindings();
        return this;
    }

    public SandboxHostBuilder AddNetworkBindings(HttpMessageInvoker? invoker = null)
    {
        _bindings.AddNetworkBindings(invoker);
        return this;
    }

    public SandboxHostBuilder AddBinding(BindingDescriptor descriptor)
    {
        _bindings.Add(descriptor);
        return this;
    }

    public SandboxHostBuilder UseInterpreter(ISandboxInterpreter? interpreter = null)
    {
        _interpreter = interpreter ?? new SandboxInterpreter();
        return this;
    }

    public SandboxHostBuilder UseCompilerIfAvailable(ISandboxCompiler? compiler = null)
    {
        _useCompiler = true;
        _compiler = compiler;
        return this;
    }

    public SandboxHostBuilder UseCompilerCache(string cacheDirectory)
    {
        _compilerCacheDirectory = cacheDirectory;
        return this;
    }

    internal SandboxHost Build()
    {
        _interpreter ??= new SandboxInterpreter();
        if (_useCompiler) {
            _compiler ??= CreateDefaultCompiler();
        }

        return new SandboxHost(_bindings.Build(), _interpreter, _compiler);
    }

    private ISandboxCompiler CreateDefaultCompiler()
    {
        var cache = _compilerCacheDirectory is null
            ? null
            : new PersistentCompiledArtifactCache(_compilerCacheDirectory);
        return new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier(), cache: cache);
    }
}
