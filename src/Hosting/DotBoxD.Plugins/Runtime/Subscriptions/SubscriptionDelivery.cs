namespace DotBoxD.Plugins.Runtime;

internal static class SubscriptionDelivery
{
    // Delivery runs on a background task (it must not block the publishing game loop), so a throwing filter or
    // handler cannot propagate to a caller and is caught here. Swallowing it silently is what makes a broken
    // RunLocal subscription look like "it just does nothing", so every caught fault is reported to the optional
    // observer before delivery is abandoned. Control flow is unchanged: a failed filter still drops the event, a
    // failed handler still lets the remaining handlers run.
    internal static async ValueTask PublishSafelyAsync<TEvent, TContext>(
        Func<TEvent, TContext, ValueTask<bool>>[] filters,
        Func<TEvent, HookContext, TContext, ValueTask>[] handlers,
        TEvent e,
        HookContext rawContext,
        TContext context,
        Action<SubscriptionDeliveryFault>? onFault)
    {
        if (rawContext.CancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            for (var i = 0; i < filters.Length; i++)
            {
                if (!await filters[i](e, context).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (rawContext.CancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Report<TEvent>(onFault, ex, SubscriptionDeliveryStage.Filter);
            return;
        }

        for (var i = 0; i < handlers.Length; i++)
        {
            if (rawContext.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await handlers[i](e, rawContext, context).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (rawContext.CancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Report<TEvent>(onFault, ex, SubscriptionDeliveryStage.Handler);
            }
        }
    }

    private static void Report<TEvent>(
        Action<SubscriptionDeliveryFault>? onFault,
        Exception exception,
        SubscriptionDeliveryStage stage)
    {
        if (onFault is null)
        {
            return;
        }

        try
        {
            onFault(new SubscriptionDeliveryFault(typeof(TEvent), stage, exception));
        }
        catch
        {
            // A faulty fault observer must never escalate into the background delivery task.
        }
    }
}
