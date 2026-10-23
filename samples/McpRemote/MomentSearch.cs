using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McpRemote;

/// <summary>A moment found in a media: where it starts, how well it matches, and what it is.</summary>
public sealed record Moment(double Time, double Score, string? Text);

/// <summary>Finds the moments of a media that match a query.</summary>
public interface IMomentSearch
{
    /// <summary>The best moments first, at most <paramref name="count"/>.</summary>
    Task<IReadOnlyList<Moment>> SearchAsync(string mediaUri, string query, int count, CancellationToken cancellationToken);
}

/// <summary>
/// Forwards a search to a local service, such as an index of what is said or
/// shown in the media. The contract is one POST of JSON:
/// <c>{"media": uri, "query": text, "count": n}</c>, answered with
/// <c>{"moments": [{"time": seconds, "score": number, "text": string}]}</c>.
/// </summary>
public sealed class HttpMomentSearch(Uri url) : IMomentSearch, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<IReadOnlyList<Moment>> SearchAsync(string mediaUri, string query, int count, CancellationToken cancellationToken)
    {
        // Buffered, with a Content-Length: JsonContent streams the body
        // chunked, which simple local servers do not read.
        string json = JsonSerializer.Serialize(new SearchRequest(mediaUri, query, count), SearchJson.Default.SearchRequest);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.PostAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"the search service answered HTTP {(int)response.StatusCode}.");
        SearchResponse? result = await response.Content.ReadFromJsonAsync(SearchJson.Default.SearchResponse, cancellationToken);
        return result?.Moments?.Take(count).ToArray() ?? [];
    }

    public void Dispose() => _http.Dispose();
}

internal sealed record SearchRequest(string Media, string Query, int Count);

internal sealed record SearchResponse(Moment[]? Moments);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SearchRequest))]
[JsonSerializable(typeof(SearchResponse))]
internal sealed partial class SearchJson : JsonSerializerContext;
