namespace DotBoxD.Plugins.Analyzer.Analysis.PluginServer;

using System.Text;

internal static partial class PluginServerFacadeEmitter
{
    private static void AppendLiveSettingsHandle(StringBuilder builder, PluginServerFacadeModel model)
    {
        builder.AppendLine("    private sealed class LiveSettingsHandle<TKernel> : global::DotBoxD.Abstractions.ILiveSettingsHandle<TKernel> where TKernel : class, new()");
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly " + PluginServerIdentifier.Escape(model.ClassName) + " _owner;");
        builder.AppendLine("        private readonly string _pluginId;");
        builder.AppendLine("        private readonly global::System.Collections.Generic.List<" + model.LiveSettingUpdateType + "> _updates = new();");
        builder.AppendLine("        public LiveSettingsHandle(" + PluginServerIdentifier.Escape(model.ClassName) + " owner, string pluginId) { _owner = owner; _pluginId = pluginId; }");
        builder.AppendLine("        public global::DotBoxD.Abstractions.ILiveSettingsHandle<TKernel> Set<TValue>(global::System.Linq.Expressions.Expression<global::System.Func<TKernel, TValue>> member, TValue value)");
        builder.AppendLine("        {");
        builder.AppendLine("            var body = member.Body is global::System.Linq.Expressions.UnaryExpression unary ? unary.Operand : member.Body;");
        builder.AppendLine("            if (body is not global::System.Linq.Expressions.MemberExpression { Member: global::System.Reflection.PropertyInfo property })");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.ArgumentException(\"Live setting expression must select a property.\", nameof(member));");
        builder.AppendLine("            }");
        builder.AppendLine("            if (!IsLiveSetting(property))");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.ArgumentException($\"Property '{property.Name}' is not a live setting.\", nameof(member));");
        builder.AppendLine("            }");
        builder.AppendLine("            _updates.Add(new " + model.LiveSettingUpdateType + "(property.Name, global::System.Convert.ToString(value, global::System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));");
        builder.AppendLine("            return this;");
        builder.AppendLine("        }");
        builder.AppendLine("        public global::System.Threading.Tasks.ValueTask ApplyAsync(bool atomic = false)");
        builder.AppendLine("            => _owner.RequireControl().UpdateSettingsAsync(_pluginId, _updates.ToArray(), atomic, default);");
        builder.AppendLine("        public async global::System.Threading.Tasks.ValueTask SetValuesAsync(global::System.Action<TKernel> set, bool atomic = false)");
        builder.AppendLine("        {");
        builder.AppendLine("            var draft = new TKernel();");
        builder.AppendLine("            var properties = typeof(TKernel).GetProperties(global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Instance)");
        builder.AppendLine("                .Where(static p => p.GetMethod is not null && p.GetIndexParameters().Length == 0)");
        builder.AppendLine("                .ToArray();");
        builder.AppendLine("            var nonLiveValues = properties");
        builder.AppendLine("                .Where(static p => !IsLiveSetting(p))");
        builder.AppendLine("                .Select(p => new { Property = p, Value = p.GetValue(draft) })");
        builder.AppendLine("                .ToArray();");
        builder.AppendLine("            set(draft);");
        builder.AppendLine("            foreach (var entry in nonLiveValues)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (!global::System.Object.Equals(entry.Value, entry.Property.GetValue(draft)))");
        builder.AppendLine("                {");
        builder.AppendLine("                    throw new global::System.InvalidOperationException($\"Property '{entry.Property.Name}' is not a live setting and cannot be written by SetValuesAsync.\");");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("            var updates = properties");
        builder.AppendLine("                .Where(static p => IsLiveSetting(p))");
        builder.AppendLine("                .Select(p => new " + model.LiveSettingUpdateType + "(p.Name, global::System.Convert.ToString(p.GetValue(draft), global::System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty))");
        builder.AppendLine("                .ToArray();");
        builder.AppendLine("            await _owner.RequireControl().UpdateSettingsAsync(_pluginId, updates, atomic, default).ConfigureAwait(false);");
        builder.AppendLine("        }");
        builder.AppendLine("        private static bool IsLiveSetting(global::System.Reflection.PropertyInfo property)");
        builder.AppendLine("            => property.GetCustomAttributes(typeof(global::DotBoxD.Abstractions.LiveSettingAttribute), inherit: true).Length != 0;");
        builder.AppendLine("    }");
    }
}
