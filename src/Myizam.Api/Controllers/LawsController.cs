using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Myizam.Data;

namespace Myizam.Api.Controllers;

[ApiController]
[Route("api/laws")]
public sealed class LawsController : ControllerBase
{
    private readonly MyizamDbContext _db;

    public LawsController(MyizamDbContext db) => _db = db;

    /// <summary>База знаний: «ассистент знает ТОЛЬКО эти документы» (§9, план 3.9).</summary>
    [HttpGet]
    public async Task<IActionResult> GetLaws(CancellationToken ct)
    {
        var laws = await _db.Laws
            .OrderBy(l => l.Title)
            .Select(l => new
            {
                code = l.DocumentCode,
                title = l.Title,
                status = l.Status,
                editionDate = l.EditionDate,
                articleCount = l.ArticleCount,
                sourceUrl = l.SourceUrl,
            })
            .ToListAsync(ct);
        return Ok(laws);
    }
}
