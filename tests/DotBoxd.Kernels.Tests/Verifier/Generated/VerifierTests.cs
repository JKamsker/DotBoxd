using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using DotBoxd.Kernels.Runtime;
using DotBoxd.Kernels.Verifier;

namespace DotBoxd.Kernels.Tests;

public sealed class VerifierTests
{
    public static TheoryData<string, Func<byte[]>, string[]> MaliciousAssemblies()
        => new() {
            { "System.IO.File.ReadAllText", FileReadAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER", "V-ASM-REF"] },
            { "System.Type.GetType", TypeGetTypeAssembly, ["V-TYPE-REF", "V-MEMBER"] },
            { "System.Reflection.Assembly.Load", AssemblyLoadAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER"] },
            { "System.Reflection.MethodInfo.Invoke", MethodInfoInvokeAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER"] },
            { "System.Environment", EnvironmentAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER"] },
            { "System.Threading.Thread", ThreadAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER"] },
            { "Activator.CreateInstance", ActivatorAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER"] },
            { "PInvoke ImplMap", PInvokeAssembly, ["V-PINVOKE", "V-METHOD-PINVOKE"] },
            { "ldtoken opcode", LdTokenAssembly, ["V-OPCODE"] },
            { "mutable static field", MutableStaticFieldAssembly, ["V-FIELD-STATIC"] },
            { "static constructor", StaticConstructorAssembly, ["V-METHOD-SPECIAL"] },
            { "unbudgeted CompiledRuntime.String", UnbudgetedStringFactoryAssembly, ["V-MEMBER"] },
            { "local helper calls System.IO.File.ReadAllText", LocalHelperFileReadAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER", "V-ASM-REF"] },
            { "object array allocation", ObjectArrayAssembly, ["V-ARRAY"] },
            { "extra public method", ExtraPublicMethodAssembly, ["V-PUBLIC-SURFACE"] },
            { "wrong Execute return type", WrongExecuteReturnTypeAssembly, ["V-EXECUTE-SIGNATURE"] },
            { "wrong Execute parameters", WrongExecuteParameterAssembly, ["V-EXECUTE-SIGNATURE"] },
            { "extra public type", ExtraPublicTypeAssembly, ["V-PUBLIC-SURFACE"] },
            { "non-static generated type", NonStaticGeneratedTypeAssembly, ["V-TYPE-SHAPE"] },
            { "synchronized method", SynchronizedMethodAssembly, ["V-METHOD-ATTR"] }
        };

    [Theory]
    [MemberData(nameof(MaliciousAssemblies))]
    public async Task Verifier_rejects_malicious_generated_assembly(
        string name,
        Func<byte[]> build,
        string[] expectedCodes)
    {
        var bytes = build();
        var result = await VerifierTestHelpers.VerifyAsync(bytes);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => expectedCodes.Contains(d.Code));
        Assert.NotEmpty(name);
    }

    private static byte[] FileReadAssembly()
        => BuildAssembly(type => {
            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(string), []);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "secret.txt");
            il.Emit(OpCodes.Call, typeof(File).GetMethod(nameof(File.ReadAllText), [typeof(string)])!);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] TypeGetTypeAssembly()
        => BuildAssembly(type => {
            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(Type), []);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "System.IO.File");
            il.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetType), [typeof(string)])!);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] AssemblyLoadAssembly()
        => BuildAssembly(type => {
            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(Assembly), []);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "System.Private.CoreLib");
            il.Emit(OpCodes.Call, typeof(Assembly).GetMethod(nameof(Assembly.Load), [typeof(string)])!);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] MethodInfoInvokeAssembly()
        => BuildAssembly(type => {
            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(object), []);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, typeof(MethodBase).GetMethod(nameof(MethodBase.Invoke), [typeof(object), typeof(object[])])!);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] EnvironmentAssembly()
        => BuildAssembly(type => {
            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(string), []);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "SECRET");
            il.Emit(OpCodes.Call, typeof(Environment).GetMethod(nameof(Environment.GetEnvironmentVariable), [typeof(string)])!);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] ThreadAssembly()
        => BuildAssembly(type => {
            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, typeof(Thread).GetMethod(nameof(Thread.Sleep), [typeof(int)])!);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] ActivatorAssembly()
        => BuildAssembly(type => {
            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(object), []);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldtoken, typeof(string));
            il.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), [typeof(RuntimeTypeHandle)])!);
            il.Emit(OpCodes.Call, typeof(Activator).GetMethod(nameof(Activator.CreateInstance), [typeof(Type)])!);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] PInvokeAssembly()
        => BuildAssembly(type => {
            type.DefinePInvokeMethod(
                "GetTickCount",
                "kernel32.dll",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.PinvokeImpl,
                CallingConventions.Standard,
                typeof(int),
                [],
                CallingConvention.Winapi,
                CharSet.Auto);
        });

    private static byte[] LdTokenAssembly()
        => BuildAssembly(type => {
            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldtoken, typeof(string));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] MutableStaticFieldAssembly()
        => BuildAssembly(type => {
            type.DefineField("State", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
            method.GetILGenerator().Emit(OpCodes.Ret);
        });

    private static byte[] StaticConstructorAssembly()
        => BuildAssembly(type => {
            var cctor = type.DefineTypeInitializer();
            cctor.GetILGenerator().Emit(OpCodes.Ret);
            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
            method.GetILGenerator().Emit(OpCodes.Ret);
        });

    private static byte[] UnbudgetedStringFactoryAssembly()
        => BuildAssembly(type => {
            var factory = typeof(CompiledRuntime).GetMethod(
                "String",
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: [typeof(string)],
                modifiers: null)!;
            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(SandboxValue), []);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "unchecked");
            il.Emit(OpCodes.Call, factory);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] LocalHelperFileReadAssembly()
        => BuildAssembly(type => {
            var helper = type.DefineMethod(
                "Read",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(string),
                []);
            var helperIl = helper.GetILGenerator();
            helperIl.Emit(OpCodes.Ldstr, "secret.txt");
            helperIl.Emit(OpCodes.Call, typeof(File).GetMethod(nameof(File.ReadAllText), [typeof(string)])!);
            helperIl.Emit(OpCodes.Ret);

            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(string), []);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Call, helper);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] ObjectArrayAssembly()
        => BuildAssembly(type => {
            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(object[]), []);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, typeof(object));
            il.Emit(OpCodes.Ret);
        });

    private static byte[] ExtraPublicMethodAssembly()
        => BuildAssembly(type => {
            DefineVoidExecute(type);
            var method = type.DefineMethod("Inspect", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
            method.GetILGenerator().Emit(OpCodes.Ret);
        });

    private static byte[] WrongExecuteReturnTypeAssembly()
        => BuildAssembly(type => {
            var method = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            method.GetILGenerator().Emit(OpCodes.Ret);
        });

    private static byte[] WrongExecuteParameterAssembly()
        => BuildAssembly(type => {
            var method = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext)]);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] ExtraPublicTypeAssembly()
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Malicious" + Guid.NewGuid().ToString("N")),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("MaliciousModule");
        var first = module.DefineType("Malicious.Module", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        DefineVoidExecute(first);
        first.CreateType();

        var second = module.DefineType("Malicious.Extra", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        DefineVoidExecute(second);
        second.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private static byte[] NonStaticGeneratedTypeAssembly()
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Malicious" + Guid.NewGuid().ToString("N")),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("MaliciousModule");
        var type = module.DefineType("Malicious.Module", TypeAttributes.Public);
        DefineVoidExecute(type);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private static byte[] SynchronizedMethodAssembly()
        => BuildAssembly(type => {
            var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
            method.SetImplementationFlags(MethodImplAttributes.Synchronized);
            method.GetILGenerator().Emit(OpCodes.Ret);
        });

    private static void DefineVoidExecute(TypeBuilder type)
    {
        var method = type.DefineMethod("Execute", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
        method.GetILGenerator().Emit(OpCodes.Ret);
    }

    private static byte[] BuildAssembly(Action<TypeBuilder> define)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("Malicious" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("MaliciousModule");
        var type = module.DefineType("Malicious.Module", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        define(type);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }
}
