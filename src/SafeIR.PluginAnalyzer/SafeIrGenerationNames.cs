namespace SafeIR.PluginAnalyzer;

internal static class SafeIrGenerationNames
{
    public const int GeneratedSpanLine = 1;
    public const int GeneratedSpanColumn = 1;
    public const string DefaultEventParameterName = "e";
    public const string DefaultContextParameterName = "ctx";
    public const string KernelSuffix = "Kernel";
    public const string PluginPackageSuffix = "PluginPackage";

    public static class KernelMethodParameters
    {
        public const int EventIndex = 0;
        public const int ContextIndex = 1;
    }

    public static class Metadata
    {
        public const string GamePluginAttribute = "SafeIR.Plugins.GamePluginAttribute";
        public const string LiveSettingAttribute = "SafeIR.Plugins.LiveSettingAttribute";
        public const string EventKernelInterface = "SafeIR.Plugins.IEventKernel<TEvent>";
        public const string RangeAttribute = "System.ComponentModel.DataAnnotations.RangeAttribute";
    }

    public static class Contracts
    {
        public const string EventKernelPrefix = "IEventKernel<";
        public const string EventKernelSuffix = ">";

        public static string EventKernel(string eventName)
            => EventKernelPrefix + eventName + EventKernelSuffix;
    }

    public static class CSharpTypes
    {
        public const string Bool = ManifestTypes.Bool;
        public const string Int = ManifestTypes.Int;
        public const string Long = ManifestTypes.Long;
        public const string Double = ManifestTypes.Double;
        public const string String = ManifestTypes.String;
    }

    public static class CSharpLiterals
    {
        public const string Null = "null";
        public const string True = "true";
        public const string False = "false";
        public const string Int32Default = "0";
        public const string Int64Default = "0L";
        public const string DoubleDefault = "0D";
        public const string StringDefault = "\"\"";
        public const string Int64Suffix = "L";
        public const string DoubleSuffix = "D";
        public const string DoubleRoundTripFormat = "R";
    }

    public static class CSharpIdentifiers
    {
        public const string EscapePrefix = "@";
    }

    public static class ManifestTypes
    {
        public const string Bool = "bool";
        public const string Int = "int";
        public const string Long = "long";
        public const string Double = "double";
        public const string String = "string";
        public const string Unsupported = "unsupported";

        public static bool IsNumeric(string type)
            => type is Int or Long or Double;
    }

    public static class RangeAttributeArguments
    {
        public const int NumericOverloadCount = 2;
        public const int TypeAndStringOverloadCount = 3;
        public const int NumericMinimumIndex = 0;
        public const int NumericMaximumIndex = 1;
        public const int ConversionTypeIndex = 0;
        public const int ConvertedMinimumIndex = 1;
        public const int ConvertedMaximumIndex = 2;
    }

    public static class Effects
    {
        public const string Cpu = "Cpu";
        public const string Alloc = "Alloc";
        public const string GameStateWrite = "GameStateWrite";
        public const string Audit = "Audit";
    }

    public static class Capabilities
    {
        public const string MessageWriteReason = "send damage notifications";
    }

    public static class Entrypoints
    {
        public const string ShouldHandle = "ShouldHandle";
        public const string Handle = "Handle";
    }

    public static class HookContext
    {
        public const string MessagesProperty = "Messages";
        public const string SendMethod = "Send";
        public const string SendTargetArgument = "targetId";
        public const string SendMessageArgument = "message";
        public const int SendTargetIndex = 0;
        public const int SendMessageIndex = 1;
    }

    public static class Helpers
    {
        public const string Var = "Var";
        public const string Str = "Str";
        public const string ConcatString = "ConcatString";
        public const string StringLength = "StringLength";
        public const string StringSubstring = "StringSubstring";
        public const string StringEquals = "StringEquals";
        public const string I32 = "I32";
        public const string I64 = "I64";
        public const string F64 = "F64";
        public const string Bool = "Bool";
        public const string Not = "Not";
        public const string Neg = "Neg";
        public const string Eq = "Eq";
        public const string Ne = "Ne";
        public const string Ge = "Ge";
        public const string Gt = "Gt";
        public const string Le = "Le";
        public const string Lt = "Lt";
        public const string And = "And";
        public const string Or = "Or";
        public const string Add = "Add";
        public const string Sub = "Sub";
        public const string Mul = "Mul";
        public const string Div = "Div";
        public const string Mod = "Mod";
    }

    public static class IrTypes
    {
        public const string IfStatement = "global::SafeIR.IfStatement";
        public const string ReturnStatement = "global::SafeIR.ReturnStatement";
    }

    public static class BindingIds
    {
        public const string StringLength = "string.length";
        public const string StringSubstringBudgeted = "string.substringBudgeted";
        public const string StringConcatBudgeted = "string.concatBudgeted";
        public const string StringEquals = "string.equals";
    }

    public static class GeneratedVariables
    {
        public const string EventPrefix = "e_";
    }

    public static class ModuleMetadata
    {
        public const string PluginId = "pluginId";
        public const string Kernel = "kernel";
    }

    public static class Operators
    {
        public const string LogicalNot = "!";
        public const string Minus = "-";
        public const string EqualTo = "==";
        public const string NotEqualTo = "!=";
        public const string GreaterThanOrEqual = ">=";
        public const string GreaterThan = ">";
        public const string LessThanOrEqual = "<=";
        public const string LessThan = "<";
        public const string LogicalAnd = "&&";
        public const string LogicalOr = "||";
        public const string Add = "+";
        public const string Multiply = "*";
        public const string Divide = "/";
        public const string Modulo = "%";
    }
}
