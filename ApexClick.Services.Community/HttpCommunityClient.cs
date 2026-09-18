using System.Net.Http.Json;
using ApexClick.Models;

namespace ApexClick.Services.Community;

public sealed class HttpCommunityClient : ICommunityClient
{
    private readonly HttpClient _http;
    public HttpCommunityClient(HttpClient http){_http=http;}
    public async Task<CommunityScriptSummary> PublishAsync(MacroScript script,string category,string author,CancellationToken cancellationToken=default)
    {
        if(!CommunityCategories.Allowed.Contains(category)) throw new InvalidOperationException("Категория запрещена.");
        using var content=new MultipartFormDataContent();
        var temp=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".mscr");
        try{await MscrScriptSerializer.SaveAsync(script,temp,cancellationToken); var bytes=await File.ReadAllBytesAsync(temp,cancellationToken); content.Add(new ByteArrayContent(bytes),"file","scenario.mscr"); content.Add(new StringContent(category),"category"); content.Add(new StringContent(author),"author"); using var response=await _http.PostAsync("publish",content,cancellationToken); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<CommunityScriptSummary>(cancellationToken:cancellationToken) ?? throw new InvalidDataException("Пустой ответ.");}
        finally{try{File.Delete(temp);}catch{}}
    }
    public async Task<IReadOnlyList<CommunityScriptSummary>> SearchAsync(string? category,string? query,CancellationToken cancellationToken=default)
    {
        var url="search"; var qs=new List<string>(); if(!string.IsNullOrWhiteSpace(category))qs.Add("category="+Uri.EscapeDataString(category)); if(!string.IsNullOrWhiteSpace(query))qs.Add("q="+Uri.EscapeDataString(query)); if(qs.Count>0) url+="?"+string.Join('&',qs);
        return await _http.GetFromJsonAsync<List<CommunityScriptSummary>>(url,cancellationToken) ?? new List<CommunityScriptSummary>();
    }
    public async Task<MacroScript> DownloadAsync(Guid scriptId,CancellationToken cancellationToken=default)
    {
        using var response=await _http.GetAsync($"download/{scriptId:N}",cancellationToken); response.EnsureSuccessStatusCode(); await using var stream=await response.Content.ReadAsStreamAsync(cancellationToken); var temp=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".mscr"); try{await using(var fs=File.Create(temp)){await stream.CopyToAsync(fs,cancellationToken);} return await MscrScriptSerializer.LoadAsync(temp,cancellationToken);}finally{try{File.Delete(temp);}catch{}}
    }
}
