namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using System.Text;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class RegistrationAccumulatorEmitter
{
    public static RegistrationGeneratedSource EmitTarget(RegistrationAccumulatorTargetModel model)
        => new(HintName(model.Namespace, model.AccumulatorName), TargetSource(model));

    public static RegistrationGeneratedSource EmitRoot(
        RegistrationRootAccumulatorModel model,
        EquatableArray<RegistrationChildAccumulatorModel> children)
        => new(HintName(model.Namespace, model.AccumulatorName), RootSource(model, children));

    private static string TargetSource(RegistrationAccumulatorTargetModel model)
    {
        var builder = SourceHeader(model.Namespace);
        builder.Append("internal sealed class ").Append(model.AccumulatorName).AppendLine();
        builder.AppendLine("{");
        builder.Append("    private readonly ").Append(model.ReceiverTypeName).AppendLine(" _target;");
        builder.Append("    private readonly global::System.Collections.Generic.List<global::System.Func<")
            .AppendLine("global::System.Threading.Tasks.ValueTask>> _registrations = [];");
        builder.AppendLine();
        builder.Append("    public ").Append(model.AccumulatorName).Append('(')
            .Append(model.ReceiverTypeName).AppendLine(" target) => _target = target;");
        builder.AppendLine();
        AppendRegistrationMethod(builder, model);
        builder.AppendLine();
        AppendFlush(builder);
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendRegistrationMethod(StringBuilder builder, RegistrationAccumulatorTargetModel model)
    {
        var genericList = GenericParameterList(model.TypeParameters);
        builder.Append("    public ").Append(model.AccumulatorName).Append(' ')
            .Append(Identifier(model.MethodName)).Append(genericList).AppendLine("()");
        AppendConstraintClauses(builder, model.TypeParameters);
        builder.AppendLine("    {");
        builder.AppendLine("        _registrations.Add(async () =>");
        builder.AppendLine("        {");
        builder.Append("            _ = await _target.").Append(Identifier(model.MethodName))
            .Append(genericList).AppendLine("().ConfigureAwait(false);");
        builder.AppendLine("        });");
        builder.AppendLine("        return this;");
        builder.AppendLine("    }");
    }

    private static string RootSource(
        RegistrationRootAccumulatorModel model,
        EquatableArray<RegistrationChildAccumulatorModel> children)
    {
        var builder = SourceHeader(model.Namespace);
        builder.Append("internal sealed class ").Append(model.AccumulatorName).AppendLine();
        builder.AppendLine("{");
        foreach (var child in children)
        {
            builder.Append("    private readonly ").Append(child.AccumulatorName).Append(' ')
                .Append(FieldName(child.PropertyName)).AppendLine(";");
        }

        builder.AppendLine();
        builder.Append("    public ").Append(model.AccumulatorName).Append('(')
            .Append(model.ReceiverTypeName).AppendLine(" target)");
        builder.AppendLine("    {");
        foreach (var child in children)
        {
            builder.Append("        ").Append(FieldName(child.PropertyName))
                .Append(" = new ").Append(child.AccumulatorName)
                .Append("(target.").Append(Identifier(child.PropertyName)).AppendLine(");");
        }

        builder.AppendLine("    }");
        foreach (var child in children)
        {
            builder.AppendLine();
            builder.Append("    public ").Append(child.AccumulatorName).Append(' ')
                .Append(Identifier(child.PropertyName)).Append(" => ")
                .Append(FieldName(child.PropertyName)).AppendLine(";");
        }

        builder.AppendLine();
        builder.AppendLine("    internal async global::System.Threading.Tasks.ValueTask FlushAsync()");
        builder.AppendLine("    {");
        foreach (var child in children)
        {
            builder.Append("        await ").Append(FieldName(child.PropertyName))
                .AppendLine(".FlushAsync().ConfigureAwait(false);");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendFlush(StringBuilder builder)
    {
        builder.AppendLine("    internal async global::System.Threading.Tasks.ValueTask FlushAsync()");
        builder.AppendLine("    {");
        builder.AppendLine("        foreach (var registration in _registrations)");
        builder.AppendLine("        {");
        builder.AppendLine("            await registration().ConfigureAwait(false);");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void AppendConstraintClauses(
        StringBuilder builder,
        EquatableArray<RegistrationTypeParameterModel> typeParameters)
    {
        foreach (var parameter in typeParameters)
        {
            if (parameter.Constraints.Count == 0)
            {
                continue;
            }

            builder.Append("        where ").Append(Identifier(parameter.Name)).Append(" : ")
                .AppendLine(string.Join(", ", parameter.Constraints));
        }
    }

    private static string GenericParameterList(EquatableArray<RegistrationTypeParameterModel> typeParameters)
    {
        if (typeParameters.Count == 0)
        {
            return string.Empty;
        }

        var names = new string[typeParameters.Count];
        for (var i = 0; i < typeParameters.Count; i++)
        {
            names[i] = Identifier(typeParameters[i].Name);
        }

        return "<" + string.Join(", ", names) + ">";
    }

    private static StringBuilder SourceHeader(string @namespace)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        if (!string.IsNullOrEmpty(@namespace))
        {
            builder.Append("namespace ").Append(@namespace).AppendLine(";");
            builder.AppendLine();
        }

        return builder;
    }

    private static string FieldName(string propertyName)
        => "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);

    private static string Identifier(string name)
    {
        var kind = SyntaxFacts.GetKeywordKind(name);
        if (kind == SyntaxKind.None)
        {
            kind = SyntaxFacts.GetContextualKeywordKind(name);
        }

        return kind == SyntaxKind.None ? name : "@" + name;
    }

    private static string HintName(string @namespace, string accumulatorName)
        => string.IsNullOrEmpty(@namespace)
            ? accumulatorName + ".g.cs"
            : @namespace.Replace("@", string.Empty) + "." + accumulatorName + ".g.cs";
}
