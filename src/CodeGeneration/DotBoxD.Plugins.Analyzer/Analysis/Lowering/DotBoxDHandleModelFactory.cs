using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDHandleModelFactory
{
    private const int SendOnlyStatementCount = 1;
    private const int SendThenReturnStatementCount = 2;
    private const int SendStatementIndex = 0;
    private const int ReturnStatementIndex = 1;
    private const string SendCallMessage = "Kernel Handle must call ctx.Messages.Send(targetId, message).";
    private const string SingleSendBodyMessage =
        "Kernel Handle must contain exactly one ctx.Messages.Send(targetId, message) call.";

    public static DotBoxDHandleModel Create(
        MethodDeclarationSyntax method,
        string eventParameterName,
        string contextParameterName,
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<string>? capabilities = null,
        ICollection<string>? effects = null)
        => CreateFromSend(
            SingleSendInvocation(method, contextParameterName),
            eventParameterName,
            eventProperties,
            liveSettings,
            semanticModel,
            cancellationToken,
            capabilities,
            effects);

    /// <summary>
    /// Lowers a single <c>ctx.Messages.Send(targetId, message)</c> invocation to a handle model. Shared
    /// by kernel <c>Handle</c> methods and lowered hook-chain <c>Run</c> terminals.
    /// </summary>
    public static DotBoxDHandleModel CreateFromSend(
        InvocationExpressionSyntax sendInvocation,
        string eventParameterName,
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ICollection<string>? capabilities = null,
        ICollection<string>? effects = null)
        => CreateFromSend(
            sendInvocation,
            new DotBoxDExpressionLoweringContext(
                eventParameterName, eventProperties, liveSettings, semanticModel, cancellationToken,
                capabilities: capabilities, effects: effects));

    /// <summary>Lowers a send using a prebuilt context (e.g. one carrying a Select-projected element).</summary>
    public static DotBoxDHandleModel CreateFromSend(
        InvocationExpressionSyntax sendInvocation,
        DotBoxDExpressionLoweringContext loweringContext)
    {
        var arguments = SendArguments(sendInvocation);
        var target = DotBoxDExpressionModelFactory.Create(arguments.Target, loweringContext);
        var message = DotBoxDExpressionModelFactory.Create(arguments.Message, loweringContext);
        RequireString(target, "targetId");
        RequireString(message, "message");
        loweringContext.Capabilities?.Add(DotBoxDGenerationNames.Capabilities.MessageWrite);
        loweringContext.Capabilities?.Add(DotBoxDGenerationNames.Capabilities.RuntimeAsync);
        loweringContext.Effects?.Add(DotBoxDGenerationNames.Effects.HostStateWrite);
        loweringContext.Effects?.Add(DotBoxDGenerationNames.Effects.Concurrency);
        loweringContext.Effects?.Add(DotBoxDGenerationNames.Effects.Audit);
        return new DotBoxDHandleModel(target, message);
    }

    /// <summary>Whether an expression is a <c>&lt;context&gt;.Messages.Send</c> member access.</summary>
    public static bool IsContextSend(ExpressionSyntax expression, string contextParameterName)
        => IsContextMessageSend(expression, contextParameterName);

    private static SendArgumentExpressions SendArguments(InvocationExpressionSyntax invocation)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 2)
        {
            throw new NotSupportedException(SendCallMessage);
        }

        ExpressionSyntax? target = null;
        ExpressionSyntax? message = null;
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            var name = argument.NameColon?.Name.Identifier.ValueText;
            if (name is null)
            {
                AssignByPosition(i, argument.Expression, ref target, ref message);
                continue;
            }

            AssignByName(name, argument.Expression, ref target, ref message);
        }

        return target is not null && message is not null
            ? new SendArgumentExpressions(target, message)
            : throw new NotSupportedException(SendCallMessage);
    }

    private static void AssignByPosition(
        int index,
        ExpressionSyntax expression,
        ref ExpressionSyntax? target,
        ref ExpressionSyntax? message)
    {
        if (index == DotBoxDGenerationNames.HookContext.SendTargetIndex)
        {
            Assign(DotBoxDGenerationNames.HookContext.SendTargetArgument, expression, ref target);
            return;
        }

        if (index == DotBoxDGenerationNames.HookContext.SendMessageIndex)
        {
            Assign(DotBoxDGenerationNames.HookContext.SendMessageArgument, expression, ref message);
            return;
        }

        throw new NotSupportedException(SendCallMessage);
    }

    private static void AssignByName(
        string name,
        ExpressionSyntax expression,
        ref ExpressionSyntax? target,
        ref ExpressionSyntax? message)
    {
        if (string.Equals(name, DotBoxDGenerationNames.HookContext.SendTargetArgument, StringComparison.Ordinal))
        {
            Assign(name, expression, ref target);
            return;
        }

        if (string.Equals(name, DotBoxDGenerationNames.HookContext.SendMessageArgument, StringComparison.Ordinal))
        {
            Assign(name, expression, ref message);
            return;
        }

        throw new NotSupportedException(SendCallMessage);
    }

    private static void Assign(string name, ExpressionSyntax expression, ref ExpressionSyntax? slot)
    {
        if (slot is not null)
        {
            throw new NotSupportedException($"Kernel Handle has duplicate ctx.Messages.Send argument '{name}'.");
        }

        slot = expression;
    }

    private static InvocationExpressionSyntax SingleSendInvocation(
        MethodDeclarationSyntax method,
        string contextParameterName)
    {
        var expression = method.ExpressionBody?.Expression;
        if (expression is null)
        {
            if (method.Body is null ||
                !TryBodySendExpression(method.Body, out expression))
            {
                throw new NotSupportedException(SingleSendBodyMessage);
            }
        }

        if (expression is not InvocationExpressionSyntax invocation ||
            !IsContextMessageSend(invocation.Expression, contextParameterName))
        {
            throw new NotSupportedException(SendCallMessage);
        }

        return invocation;
    }

    private static bool TryBodySendExpression(BlockSyntax body, out ExpressionSyntax? expression)
    {
        expression = null;
        if (body.Statements.Count == SendOnlyStatementCount &&
            body.Statements[SendStatementIndex] is ExpressionStatementSyntax sendOnly)
        {
            expression = sendOnly.Expression;
            return true;
        }

        if (body.Statements.Count == SendThenReturnStatementCount &&
            body.Statements[SendStatementIndex] is ExpressionStatementSyntax sendBeforeReturn &&
            body.Statements[ReturnStatementIndex] is ReturnStatementSyntax { Expression: null })
        {
            expression = sendBeforeReturn.Expression;
            return true;
        }

        return false;
    }

    private static bool IsContextMessageSend(ExpressionSyntax expression, string contextParameterName)
    {
        if (expression is not MemberAccessExpressionSyntax sendAccess ||
            !string.Equals(sendAccess.Name.Identifier.ValueText, DotBoxDGenerationNames.HookContext.SendMethod, StringComparison.Ordinal) ||
            sendAccess.Expression is not MemberAccessExpressionSyntax messagesAccess ||
            !string.Equals(messagesAccess.Name.Identifier.ValueText, DotBoxDGenerationNames.HookContext.MessagesProperty, StringComparison.Ordinal))
        {
            return false;
        }

        return messagesAccess.Expression is IdentifierNameSyntax context &&
            string.Equals(context.Identifier.ValueText, contextParameterName, StringComparison.Ordinal);
    }

    private static void RequireString(DotBoxDExpressionModel expression, string argumentName)
    {
        if (!string.Equals(expression.Type, DotBoxDGenerationNames.ManifestTypes.String, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"Kernel Handle {argumentName} argument must lower to a string expression.");
        }
    }

    private sealed record SendArgumentExpressions(ExpressionSyntax Target, ExpressionSyntax Message);
}
