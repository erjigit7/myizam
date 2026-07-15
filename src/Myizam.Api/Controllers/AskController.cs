using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Myizam.Core.Models;
using Myizam.Core.Services;
using Myizam.Data;

namespace Myizam.Api.Controllers;

[ApiController]
[Route("api/ask")]
public sealed class AskController : ControllerBase
{
    private readonly AskService _ask;
    private readonly MyizamDbContext _db;
    private readonly RateLimitConfig _rateLimit;

    public AskController(AskService ask, MyizamDbContext db, RateLimitConfig rateLimit)
    {
        _ask = ask;
        _db = db;
        _rateLimit = rateLimit;
    }

    [HttpPost]
    [EnableRateLimiting("ask-burst")]
    public async Task<ActionResult<AskResponse>> Ask([FromBody] AskRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { error = "Вопрос пуст" });
        if (request.Question.Length > 2000)
            return BadRequest(new { error = "Вопрос слишком длинный (максимум 2000 символов)" });

        var clientHash = (string)HttpContext.Items["client_hash"]!;

        // Суточный лимит по query_log (§12): точные Remaining/Reset в заголовках
        var since = DateTimeOffset.UtcNow.AddDays(-1);
        var windowQueries = await _db.QueryLog
            .Where(q => q.ClientHash == clientHash && q.CreatedAt >= since)
            .OrderBy(q => q.CreatedAt)
            .Select(q => q.CreatedAt)
            .ToListAsync(ct);

        var remaining = Math.Max(0, _rateLimit.DailyLimit - windowQueries.Count);
        var reset = windowQueries.Count > 0 ? windowQueries[0].AddDays(1) : DateTimeOffset.UtcNow.AddDays(1);
        Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, remaining - 1).ToString();
        Response.Headers["X-RateLimit-Reset"] = reset.ToUnixTimeSeconds().ToString();

        if (remaining == 0)
        {
            Response.Headers["X-RateLimit-Remaining"] = "0";
            return StatusCode(429, new
            {
                error = "Дневной лимит вопросов исчерпан. Попробуйте завтра. / Суроолордун күндүк лимити түгөндү. Эртең кайра аракет кылыңыз. / Daily question limit reached, try again tomorrow.",
            });
        }

        var response = await _ask.AskAsync(request.Question.Trim(), clientHash, ct);
        return Ok(response);
    }
}
