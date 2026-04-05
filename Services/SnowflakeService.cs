using System.Text;
using System.Text.Json;
using EverBankSearchApi.Models;

namespace EverBankSearchApi.Services;

public class SnowflakeService
{
    private readonly HttpClient _http;
    private readonly string _snowflakeUrl;
    private readonly string _pat;
    private readonly string _warehouse;
    private readonly string _role;
    private readonly string _cortexService;

    public SnowflakeService(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _http = httpClientFactory.CreateClient();
        _snowflakeUrl = config["Snowflake:Url"]!;
        _pat = config["Snowflake:Pat"]!;
        _warehouse = config["Snowflake:Warehouse"] ?? "COMPUTE_WH";
        _role = config["Snowflake:Role"] ?? "ACCOUNTADMIN";
        _cortexService = config["Snowflake:CortexService"] ?? "EVERBANK_DOCS.RAG.DOC_SEARCH";
    }

    public async Task<List<SearchResult>> SearchAsync(string query, string mode)
    {
        var filterClause = mode switch
        {
            "website" => ", \"filter\": {\"@eq\": {\"SOURCE_TYPE\": \"WEB\"}}",
            "documents" => ", \"filter\": {\"@eq\": {\"SOURCE_TYPE\": \"FILE\"}}",
            _ => ""
        };

        var cortexQuery = $$$"""{"query": "{{{query}}}", "columns": ["FILE_NAME", "FILE_CONTENT", "SOURCE_URL", "SOURCE_TYPE"], "limit": 4{{{filterClause}}}}""";
        var sql = $"SELECT SNOWFLAKE.CORTEX.SEARCH_PREVIEW('{_cortexService}', '{cortexQuery}')";

        var body = JsonSerializer.Serialize(new
        {
            statement = sql,
            warehouse = _warehouse,
            role = _role
        });

        var request = new HttpRequestMessage(HttpMethod.Post, _snowflakeUrl);
        request.Headers.Add("Authorization", $"Bearer {_pat}");
        request.Headers.Add("X-Snowflake-Authorization-Token-Type", "PROGRAMMATIC_ACCESS_TOKEN");
        request.Headers.Add("Accept", "application/json");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Snowflake returned {(int)response.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var dataEl))
            return [];

        // Result is in data[0][0] as a JSON string
        var rawValue = dataEl[0][0].GetString();

        if (string.IsNullOrEmpty(rawValue))
            return [];

        using var parsed = JsonDocument.Parse(rawValue);
        var resultsArray = parsed.RootElement.GetProperty("results");

        var results = new List<SearchResult>();
        var seen = new HashSet<string>();

        foreach (var r in resultsArray.EnumerateArray())
        {
            var url = r.TryGetProperty("SOURCE_URL", out var u) ? u.GetString() ?? "" : "";
            if (!seen.Add(url)) continue; // deduplicate by URL

            var sourceType = r.TryGetProperty("SOURCE_TYPE", out var st) ? st.GetString() ?? "" : "";
            var isFile = sourceType.Equals("FILE", StringComparison.OrdinalIgnoreCase);
            var fileName = r.TryGetProperty("FILE_NAME", out var fn) ? fn.GetString() ?? "" : "";
            var content = r.TryGetProperty("FILE_CONTENT", out var fc) ? fc.GetString() ?? "" : "";

            results.Add(new SearchResult
            {
                Title = BuildTitle(url, isFile, fileName),
                Url = url,
                Snippet = content.Length > 600 ? content[..600] : content,
                IsFile = isFile
            });
        }

        return results;
    }

    private static string BuildTitle(string url, bool isFile, string fileName)
    {
        if (isFile && !string.IsNullOrEmpty(fileName))
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            return ToTitleCase(name.Replace('-', ' ').Replace('_', ' '));
        }

        try
        {
            var path = new Uri(url).AbsolutePath.TrimEnd('/');
            var segment = path.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s));
            return segment != null ? ToTitleCase(segment.Replace('-', ' ')) : new Uri(url).Host;
        }
        catch
        {
            return url;
        }
    }

    private static string ToTitleCase(string s) =>
        string.Join(' ', s.Split(' ').Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
}
