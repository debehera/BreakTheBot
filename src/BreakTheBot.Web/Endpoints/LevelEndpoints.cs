using System.Security.Claims;
using BreakTheBot.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace BreakTheBot.Web.Endpoints;

public record ChatRequest(string? Message, bool DefenseOn);
public record ApiError(string Error, string Message);

public static class LevelEndpoints
{
    public static void MapLevelEndpoints(this WebApplication app)
    {
        // Everything under /api/levels needs a logged-in user and a valid anti-forgery token.
        var api = app.MapGroup("/api/levels")
            .RequireAuthorization()
            .AddEndpointFilter(async (ctx, next) =>
            {
                var antiforgery = ctx.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
                try
                {
                    await antiforgery.ValidateRequestAsync(ctx.HttpContext);
                }
                catch (AntiforgeryValidationException)
                {
                    return Results.Json(
                        new ApiError("antiforgery_failed", "Your session token is invalid. Refresh the page and try again."),
                        statusCode: StatusCodes.Status403Forbidden);
                }
                return await next(ctx);
            });

        api.MapPost("/{id:int}/chat", async (
                int id, ChatRequest request, HttpContext http, LevelEngine engine, CancellationToken ct) =>
            {
                // The user always comes from the login cookie, never from the request body.
                var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId == null)
                    return Results.Json(new ApiError("unauthorized", "Please log in."), statusCode: 401);

                var outcome = await engine.ChatAsync(userId, id, request.Message, request.DefenseOn, ct);

                if (!outcome.Success)
                {
                    var status = outcome.ErrorCode switch
                    {
                        "message_empty" or "message_too_long" => StatusCodes.Status400BadRequest,
                        "level_not_found" => StatusCodes.Status404NotFound,
                        "daily_cap_user" or "daily_cap_global" => StatusCodes.Status429TooManyRequests,
                        "llm_quota" or "safety_blocked" or "llm_unavailable" => StatusCodes.Status502BadGateway,
                        _ => StatusCodes.Status500InternalServerError
                    };
                    return Results.Json(
                        new ApiError(outcome.ErrorCode ?? "error", outcome.Message ?? "Something went wrong."),
                        statusCode: status);
                }

                return Results.Ok(new
                {
                    reply = outcome.Reply,
                    systemNote = outcome.SystemNote,
                    blocked = outcome.Blocked,
                    exploitDetected = outcome.ExploitDetected,
                    usage = new { inputTokens = outcome.InputTokens, outputTokens = outcome.OutputTokens },
                    quota = new { userRemainingToday = outcome.Quota?.UserRemainingToday ?? 0 }
                });
            })
            .RequireRateLimiting("chat");
    }
}