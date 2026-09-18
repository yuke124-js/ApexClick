using ApexClick.Models;

namespace ApexClick.FeaturePack.AI;

[Obsolete("Use ApexClick.Services.AI.PromptInterpreterService. This adapter is retained for compatibility.")]
public sealed class PromptInterpreterService : IDisposable
{
    private readonly ApexClick.Services.AI.PromptInterpreterService _inner;
    public PromptInterpreterService(ApexClick.Services.AI.PromptInterpreterService inner) => _inner = inner;
    public Task<MacroScript> GenerateAsync(string prompt, CancellationToken ct = default) =>
        _inner.GenerateFromPromptAsync(prompt, ct);
    public void Dispose() { }
}
