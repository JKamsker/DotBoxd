namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

using DotBoxD.Plugins.Analyzer.Analysis;

internal sealed record RegistrationAccumulatorTargetModel(
    string Namespace,
    string ReceiverTypeName,
    string AccumulatorName,
    string MethodName,
    EquatableArray<RegistrationTypeParameterModel> TypeParameters,
    PluginDiagnosticLocation Location);

internal sealed record RegistrationTypeParameterModel(
    string Name,
    EquatableArray<string> Constraints);

internal sealed record RegistrationRootAccumulatorModel(
    string Namespace,
    string ReceiverTypeName,
    string AccumulatorName,
    EquatableArray<RegistrationRootPropertyModel> Properties,
    PluginDiagnosticLocation Location);

internal sealed record RegistrationRootPropertyModel(
    string Name,
    string DeclaringTypeName,
    EquatableArray<string> AssignableReceiverTypeNames,
    bool GetterAccessibleFromGeneratedAccumulator);

internal sealed record RegistrationChildAccumulatorModel(
    string PropertyName,
    string DeclaringTypeName,
    string AccumulatorName);

internal sealed record RegistrationAccumulatorGenerationResult(
    RegistrationAccumulatorTargetModel? Target,
    RegistrationRootAccumulatorModel? Root,
    PluginKernelDiagnostic? Diagnostic);

internal sealed record RegistrationGeneratedSource(string HintName, string Source);

internal sealed record RegistrationGenerationBatch(
    EquatableArray<RegistrationGeneratedSource> Sources,
    EquatableArray<PluginKernelDiagnostic> Diagnostics);
