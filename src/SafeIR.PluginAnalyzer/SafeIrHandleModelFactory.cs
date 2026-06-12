namespace SafeIR.PluginAnalyzer;

using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class SafeIrHandleModelFactory
{
    public static SafeIrHandleModel Create(
        MethodDeclarationSyntax method,
        string eventParameterName,
        string contextParameterName,
        EquatableArray<EventPropertyModel> eventProperties,
        EquatableArray<LiveSettingModel> liveSettings)
    {
        var invocation = SingleSendInvocation(method, contextParameterName);
        if (invocation.ArgumentList.Arguments.Count != 2) {
            throw new NotSupportedException("Kernel Handle must call ctx.Messages.Send(targetId, message).");
        }

        var target = SafeIrExpressionModelFactory.Create(
            invocation.ArgumentList.Arguments[0].Expression,
            eventParameterName,
            eventProperties,
            liveSettings);
        var message = SafeIrExpressionModelFactory.Create(
            invocation.ArgumentList.Arguments[1].Expression,
            eventParameterName,
            eventProperties,
            liveSettings);
        RequireString(target, "targetId");
        RequireString(message, "message");
        return new SafeIrHandleModel(target, message);
    }

    private static InvocationExpressionSyntax SingleSendInvocation(
        MethodDeclarationSyntax method,
        string contextParameterName)
    {
        var expression = method.ExpressionBody?.Expression;
        if (expression is null) {
            if (method.Body is null ||
                method.Body.Statements.Count != 1 ||
                method.Body.Statements[0] is not ExpressionStatementSyntax statement) {
                throw new NotSupportedException(
                    "Kernel Handle must contain exactly one ctx.Messages.Send(targetId, message) call.");
            }

            expression = statement.Expression;
        }

        if (expression is not InvocationExpressionSyntax invocation ||
            !IsContextMessageSend(invocation.Expression, contextParameterName)) {
            throw new NotSupportedException("Kernel Handle must call ctx.Messages.Send(targetId, message).");
        }

        return invocation;
    }

    private static bool IsContextMessageSend(ExpressionSyntax expression, string contextParameterName)
    {
        if (expression is not MemberAccessExpressionSyntax sendAccess ||
            !string.Equals(sendAccess.Name.Identifier.ValueText, "Send", StringComparison.Ordinal) ||
            sendAccess.Expression is not MemberAccessExpressionSyntax messagesAccess ||
            !string.Equals(messagesAccess.Name.Identifier.ValueText, "Messages", StringComparison.Ordinal)) {
            return false;
        }

        return messagesAccess.Expression is IdentifierNameSyntax context &&
            string.Equals(context.Identifier.ValueText, contextParameterName, StringComparison.Ordinal);
    }

    private static void RequireString(SafeIrExpressionModel expression, string argumentName)
    {
        if (!string.Equals(expression.Type, "string", StringComparison.Ordinal)) {
            throw new NotSupportedException(
                $"Kernel Handle {argumentName} argument must lower to a string expression.");
        }
    }
}
