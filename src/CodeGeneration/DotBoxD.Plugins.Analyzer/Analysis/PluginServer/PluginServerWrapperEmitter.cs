using System.Text;

namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

internal static class PluginServerWrapperEmitter
{
    public static void AppendControlWrapper(
        StringBuilder builder,
        PluginServerFacadeModel model,
        PluginServerControlProperty control)
    {
        PluginServerXmlDocumentation.Append(builder, "    ", control.Documentation);
        builder.AppendLine("    public sealed class " + control.WrapperName + " : " + control.Type + ", global::DotBoxD.Abstractions.IServerExtensionClientAccessor");
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly " + PluginServerIdentifier.Escape(model.ClassName) + " _owner;");
        builder.AppendLine("        private readonly " + control.Type + " _inner;");
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            "        ",
            "Creates a generated wrapper around the remote domain control and the owning plugin server.");
        builder.AppendLine("        public " + control.WrapperName + "(" + PluginServerIdentifier.Escape(model.ClassName) + " owner, " + control.Type + " inner) { _owner = owner; _inner = inner; }");
        AppendAccessorSurface(builder, "        ");
        foreach (var property in control.Properties)
        {
            AppendProperty(builder, property, "        ");
        }

        foreach (var method in control.Methods)
        {
            AppendMethod(builder, method, "        ");
        }

        foreach (var wrapper in control.ServiceWrappers)
        {
            AppendServiceWrapper(builder, model, wrapper);
        }

        builder.AppendLine("    }");
    }

    private static void AppendServiceWrapper(
        StringBuilder builder,
        PluginServerFacadeModel model,
        PluginServerServiceWrapper wrapper)
    {
        PluginServerXmlDocumentation.Append(builder, "        ", wrapper.Documentation);
        builder.AppendLine("        private sealed class " + wrapper.WrapperName + " : " + wrapper.Type + ", global::DotBoxD.Abstractions.IServerExtensionClientAccessor");
        builder.AppendLine("        {");
        builder.AppendLine("            private readonly " + PluginServerIdentifier.Escape(model.ClassName) + " _owner;");
        builder.AppendLine("            private readonly " + wrapper.Type + " _inner;");
        builder.AppendLine("            public " + wrapper.WrapperName + "(" + PluginServerIdentifier.Escape(model.ClassName) + " owner, " + wrapper.Type + " inner) { _owner = owner; _inner = inner; }");
        AppendAccessorSurface(builder, "            ");
        foreach (var property in wrapper.Properties)
        {
            AppendProperty(builder, property, "            ");
        }

        foreach (var method in wrapper.Methods)
        {
            AppendMethod(builder, method, "            ");
        }

        builder.AppendLine("        }");
    }

    private static void AppendAccessorSurface(StringBuilder builder, string indent)
    {
        PluginServerXmlDocumentation.AppendSummary(
            builder,
            indent,
            "Registry for server extension clients installed through setup, Extend, or EnsureAnonymousKernelAsync.");
        builder.Append(indent)
            .AppendLine("global::DotBoxD.Abstractions.IServerExtensionClientRegistry global::DotBoxD.Abstractions.IServerExtensionClientAccessor.ServerExtensions => _owner;");
    }

    private static void AppendProperty(StringBuilder builder, PluginServerForwardedProperty property, string indent)
    {
        PluginServerXmlDocumentation.Append(builder, indent, property.Documentation);
        PluginServerFlowAttributeSource.Append(builder, indent, property.Attributes);
        builder.Append(indent).Append("public ").Append(property.Type).Append(' ')
            .Append(PluginServerIdentifier.Escape(property.Name)).Append(" => ");
        if (property.ReturnWrapperName is null)
        {
            builder.Append("_inner.").Append(PluginServerIdentifier.Escape(property.Name)).AppendLine(";");
            return;
        }

        builder.Append("new ").Append(property.ReturnWrapperName).Append("(_owner, _inner.")
            .Append(PluginServerIdentifier.Escape(property.Name)).AppendLine(");");
    }

    private static void AppendMethod(StringBuilder builder, PluginServerForwardedMethod method, string indent)
    {
        PluginServerXmlDocumentation.Append(builder, indent, method.Documentation);
        PluginServerFlowAttributeSource.Append(builder, indent, method.ReturnAttributes);
        builder.Append(indent).Append("public ");
        if (method.ReturnWrapperKind is PluginServerReturnWrapperKind.Task or PluginServerReturnWrapperKind.ValueTask)
        {
            builder.Append("async ");
        }

        builder.Append(method.ReturnType).Append(' ').Append(PluginServerIdentifier.Escape(method.Name))
            .Append('(').Append(ParameterList(method)).Append(") => ");
        if (method.ReturnWrapperName is null)
        {
            builder.Append("((").Append(method.ReceiverType).Append(")_inner).")
                .Append(PluginServerIdentifier.Escape(method.Name))
                .Append('(').Append(ArgumentList(method)).AppendLine(");");
            return;
        }

        if (method.ReturnWrapperKind is PluginServerReturnWrapperKind.Task or PluginServerReturnWrapperKind.ValueTask)
        {
            builder.Append("new ").Append(method.ReturnWrapperName).Append("(_owner, await ((")
                .Append(method.ReceiverType).Append(")_inner).")
                .Append(PluginServerIdentifier.Escape(method.Name)).Append('(').Append(ArgumentList(method))
                .AppendLine(").ConfigureAwait(false));");
            return;
        }

        builder.Append("new ").Append(method.ReturnWrapperName).Append("(_owner, ((")
            .Append(method.ReceiverType).Append(")_inner).")
            .Append(PluginServerIdentifier.Escape(method.Name)).Append('(').Append(ArgumentList(method)).AppendLine("));");
    }

    private static string ParameterList(PluginServerForwardedMethod method)
        => string.Join(", ", method.Parameters.Select(static p => ParamsModifier(p) + p.Type + " @" + p.Name + p.DefaultClause));

    private static string ParamsModifier(PluginServerParameter parameter)
        => parameter.IsParams ? "params " : string.Empty;

    private static string ArgumentList(PluginServerForwardedMethod method)
        => string.Join(", ", method.Parameters.Select(static p => "@" + p.Name));
}
