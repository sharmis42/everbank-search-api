namespace EverBankSearchApi.Models;

public class SearchRequest
{
    public string Query { get; set; } = "";
    // "all" | "website" | "documents"
    public string Mode { get; set; } = "all";
}
