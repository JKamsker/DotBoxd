using System.Text;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static partial class ProxyGenerator
{
    private static bool HasStreamedRequestParameter(MethodModel method, CancellationToken ct)
    {
        foreach (var parameter in method.Parameters.Array)
        {
            ct.ThrowIfCancellationRequested();
            if (parameter.StreamKind != ParameterStreamKind.None)
            {
                return true;
            }
        }

        return false;
    }

    private static void EmitLazyAsyncEnumerableInvocation(
        StringBuilder sb,
        ServiceModel service,
        MethodModel method,
        string ctArg,
        GeneratedLocalNames locals,
        CancellationToken ct)
    {
        var iteratorName = locals.Reserve("__dotboxd_enumerate", ct);
        var enumerationCt = locals.Reserve("__dotboxd_enumerationCt", ct);
        var sequenceName = locals.Reserve("__dotboxd_sequence", ct);
        var enumeratorName = locals.Reserve("__dotboxd_enumerator", ct);
        var iteratorArgument = ctArg == "default" ? string.Empty : ctArg;
        sb.AppendLine($"            return {iteratorName}({iteratorArgument});");
        sb.AppendLine();
        sb.AppendLine($"            async {method.DeclaredReturnType} {iteratorName}([{ServicesGeneratorTypeNames.GlobalEnumeratorCancellationAttribute}] {ServicesGeneratorTypeNames.GlobalCancellationToken} {enumerationCt} = default)");
        sb.AppendLine("            {");
        var invocation = BuildClientInvocation(
            sb,
            service,
            method,
            enumerationCt,
            locals,
            ct,
            "                ");
        ProxyInvocationCleanupEmitter.EmitInvocationAssignment(
            sb,
            method.DeclaredReturnType,
            sequenceName,
            invocation.Invocation,
            invocation.Reservations,
            locals,
            ct,
            "                ");
        sb.AppendLine($"                var {enumeratorName} = {sequenceName}.GetAsyncEnumerator({enumerationCt});");
        sb.AppendLine("                try");
        sb.AppendLine("                {");
        sb.AppendLine($"                    while (await {enumeratorName}.MoveNextAsync())");
        sb.AppendLine("                    {");
        sb.AppendLine($"                        yield return {enumeratorName}.Current;");
        sb.AppendLine("                    }");
        sb.AppendLine("                }");
        sb.AppendLine("                finally");
        sb.AppendLine("                {");
        sb.AppendLine($"                    await {enumeratorName}.DisposeAsync();");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
    }
}
