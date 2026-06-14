namespace SafeIR.Example.PluginAuthoring;

internal static class ManifestInspectionExample
{
    public static void Run()
    {
        var package = FireDamagePluginPackage.Create();

        Console.WriteLine($"manifest: plugin={package.Manifest.PluginId}");
        foreach (var setting in package.Manifest.LiveSettings) {
            var range = setting.Min is null && setting.Max is null
                ? string.Empty
                : $" range=[{setting.Min}..{setting.Max}]";
            Console.WriteLine($"  setting {setting.Name}: {setting.Type} = {setting.DefaultValue}{range}");
        }

        foreach (var capability in package.Module.CapabilityRequests) {
            var reason = capability.Reason is null ? string.Empty : $" ({capability.Reason})";
            Console.WriteLine($"  capability {capability.Id}{reason}");
        }

        foreach (var effect in package.Manifest.Effects) {
            Console.WriteLine($"  effect {effect}");
        }

        foreach (var subscription in package.Manifest.Subscriptions) {
            Console.WriteLine($"  subscription {subscription.Event} -> {subscription.Kernel}");
        }
    }
}
