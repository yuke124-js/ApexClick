namespace ApexClick.Services.Community;

public static class CommunityCategories
{
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(new[]{"productivity","software-testing","accessibility","education-demo"},StringComparer.OrdinalIgnoreCase);
}

public sealed record CommunityScriptSummary(Guid Id,string Name,string Category,int Downloads,string Author);
public sealed record CommunityPublishResult(bool Accepted,string Status,string? Reason);
