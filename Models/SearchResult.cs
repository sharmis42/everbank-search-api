namespace EverBankSearchApi.Models;

public class SearchResult
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Snippet { get; set; } = "";
    public bool IsFile { get; set; }
}
