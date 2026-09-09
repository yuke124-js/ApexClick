using ApexClick.Models;
namespace ApexClick.Services.Community;

public interface ICommunityClient
{
    Task<CommunityScriptSummary> PublishAsync(MacroScript script,string category,string author,CancellationToken cancellationToken=default);
    Task<IReadOnlyList<CommunityScriptSummary>> SearchAsync(string? category,string? query,CancellationToken cancellationToken=default);
    Task<MacroScript> DownloadAsync(Guid scriptId,CancellationToken cancellationToken=default);
}
