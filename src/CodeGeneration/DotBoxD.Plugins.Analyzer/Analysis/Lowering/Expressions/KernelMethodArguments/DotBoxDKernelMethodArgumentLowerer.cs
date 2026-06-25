using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static class DotBoxDKernelMethodArgumentLowerer
{
    public static DotBoxDExpressionModel? TryLowerWholeEvent(
        IParameterSymbol parameter,
        ExpressionSyntax expression,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.EventParameterName.Length == 0 ||
            expression is not IdentifierNameSyntax identifier ||
            !string.Equals(identifier.Identifier.ValueText, context.EventParameterName, StringComparison.Ordinal) ||
            parameter.Type is not INamedTypeSymbol recordType ||
            !string.Equals(
                SandboxTypeSourceEmitter.ManifestTag(recordType),
                DotBoxDGenerationNames.ManifestTypes.Record,
                StringComparison.Ordinal) ||
            SandboxTypeSourceEmitter.TryEmit(recordType) is not { } recordTypeSource)
        {
            return null;
        }

        var fields = DotBoxDRpcTypeMapper.RecordFields(recordType);
        var fieldSources = new string[fields.Count];
        for (var i = 0; i < fields.Count; i++)
        {
            var property = EventProperty(fields[i].Name, context);
            var fieldTag = SandboxTypeSourceEmitter.ManifestTag(fields[i].Type);
            if (property is null ||
                !string.Equals(property.Type, fieldTag, StringComparison.Ordinal))
            {
                return null;
            }

            CollectPropertyCapability(recordType, fields[i].Name, context);
            fieldSources[i] = EventPropertySource(fields[i].Name);
        }

        return new DotBoxDExpressionModel(
            DotBoxDRecordCreationExpressionLowerer.RecordNew(fieldSources, recordTypeSource),
            DotBoxDGenerationNames.ManifestTypes.Record,
            true);
    }

    private static EventPropertyModel? EventProperty(string name, DotBoxDExpressionLoweringContext context)
    {
        for (var i = 0; i < context.EventProperties.Count; i++)
        {
            var property = context.EventProperties[i];
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                return property;
            }
        }

        return null;
    }

    private static string EventPropertySource(string propertyName)
        => $"{DotBoxDGenerationNames.Helpers.Var}(" +
           $"{LiteralReader.StringLiteral(DotBoxDExpressionModelFactory.EventVariable(propertyName))})";

    private static void CollectPropertyCapability(
        INamedTypeSymbol recordType,
        string propertyName,
        DotBoxDExpressionLoweringContext context)
    {
        if (context.Capabilities is null)
        {
            return;
        }

        foreach (var property in recordType.GetMembers(propertyName).OfType<IPropertySymbol>())
        {
            foreach (var attribute in property.GetAttributes())
            {
                if (string.Equals(
                        attribute.AttributeClass?.ToDisplayString(),
                        DotBoxDMetadataNames.CapabilityAttribute,
                        StringComparison.Ordinal) &&
                    attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is string id &&
                    !string.IsNullOrEmpty(id))
                {
                    context.Capabilities.Add(id);
                }
            }
        }
    }
}
