namespace ApexClick.Services.Plugins;

public sealed record PluginInfo(string TypeId, string DisplayName, string AssemblyName);

public static class PluginRegistryExtensions
{
    public static IReadOnlyList<PluginInfo> GetInfo(this PluginRegistry registry) =>
        registry.Items.Select(p => new PluginInfo(p.TypeId, p.DisplayName, p.GetType().Assembly.GetName().Name ?? "unknown")).ToArray();
}
