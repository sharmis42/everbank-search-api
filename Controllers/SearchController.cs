using Microsoft.AspNetCore.Mvc;
using EverBankSearchApi.Models;
using EverBankSearchApi.Services;

namespace EverBankSearchApi.Controllers;

[ApiController]
[Route("[controller]")]
public class SearchController : ControllerBase
{
    private readonly SnowflakeService _snowflake;

    public SearchController(SnowflakeService snowflake)
    {
        _snowflake = snowflake;
    }

    [HttpPost]
    public async Task<IActionResult> Search([FromBody] SearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "Query is required." });

        var results = await _snowflake.SearchAsync(request.Query, request.Mode);
        return Ok(new { results });
    }
}
