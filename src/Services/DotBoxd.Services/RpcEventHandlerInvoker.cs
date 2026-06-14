namespace DotBoxd.Services;

internal static class RpcEventHandlerInvoker
{
    public static void Raise<TEventArgs>(
        EventHandler<TEventArgs>? handler,
        object sender,
        TEventArgs args)
        where TEventArgs : EventArgs
    {
        if (handler is null)
        {
            return;
        }

        try
        {
            handler.Invoke(sender, args);
        }
        catch (Exception ex)
        {
            RaiseWithIsolation(handler, sender, args, ex);
        }
    }

    private static void RaiseWithIsolation<TEventArgs>(
        EventHandler<TEventArgs> handler,
        object sender,
        TEventArgs args,
        Exception firstError)
        where TEventArgs : EventArgs
    {
        var subscribers = handler.GetInvocationList();
        if (subscribers.Length == 1)
        {
            RpcDiagnostics.Report(
                $"Event handler '{subscribers[0].Method.DeclaringType?.FullName}.{subscribers[0].Method.Name}' failed",
                firstError);
            return;
        }

        foreach (var subscriber in subscribers)
        {
            try
            {
                ((EventHandler<TEventArgs>)subscriber).Invoke(sender, args);
            }
            catch (Exception ex)
            {
                RpcDiagnostics.Report(
                    $"Event handler '{subscriber.Method.DeclaringType?.FullName}.{subscriber.Method.Name}' failed",
                    ex);
            }
        }
    }
}
