namespace ApexClick.Services.Plugins;

public sealed record PluginContext(IReadOnlyDictionary<string, string> Variables, IServiceProvider? Services = null);
public sealed record PluginResult(bool Success, string? Output = null);

public interface IGraphNodePlugin
{
    string TypeId { get; }
    string DisplayName { get; }
    Task<PluginResult> ExecuteAsync(PluginContext context, CancellationToken cancellationToken = default);
}
