using System.Security.Claims;
using BreakTheBot.Web.Levels;
using BreakTheBot.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace BreakTheBot.Web.Endpoints;

public record ChatRequest(string? Message, bool DefenseOn);
public record FlagRequest(string? Flag);
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

        // ---- chat -------------------------------------------------------
        api.MapPost("/{id:int}/chat", async (
                int id, ChatRequest request, HttpContext http, LevelEngine engine, CancellationToken ct) =>
            {
                var userId = UserId(http);
                if (userId == null) return Unauthorized();

                var outcome = await engine.ChatAsync(userId, id, request.Message, request.DefenseOn, ct);
                if (!outcome.Success) return Error(outcome.ErrorCode, outcome.Message);

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

        // ---- submit a flag ----------------------------------------------
        api.MapPost("/{id:int}/flag", async (
                int id, FlagRequest request, HttpContext http, LevelEngine engine) =>
            {
                var userId = UserId(http);
                if (userId == null) return Unauthorized();

                var outcome = await engine.SubmitFlagAsync(userId, id, request.Flag);
                if (!outcome.Success) return Error(outcome.ErrorCode, outcome.Message);

                return Results.Ok(new
                {
                    correct = outcome.Correct,
                    points = outcome.Points,
                    alreadyCaptured = outcome.AlreadyCaptured,
                    explanationUnlocked = outcome.Correct
                });
            })
            .RequireRateLimiting("flag");

        // ---- reveal the next hint ---------------------------------------
        api.MapPost("/{id:int}/hint", async (int id, HttpContext http, LevelEngine engine) =>
        {
            var userId = UserId(http);
            if (userId == null) return Unauthorized();

            var outcome = await engine.HintAsync(userId, id);
            if (!outcome.Success) return Error(outcome.ErrorCode, outcome.Message);

            return Results.Ok(new
            {
                hint = outcome.Hint,
                hintsUsed = outcome.HintsUsed,
                hintsRemaining = outcome.HintsRemaining,
                nextHintCost = outcome.NextHintCost
            });
        });

        // ---- reset the conversation -------------------------------------
        api.MapPost("/{id:int}/reset", async (
            int id, HttpContext http, LevelEngine engine, LevelRegistry levels, CancellationToken ct) =>
        {
            var userId = UserId(http);
            if (userId == null) return Unauthorized();
            if (levels.GetById(id) == null) return Error("level_not_found", "That level does not exist.");

            await engine.ResetAsync(userId, id, ct);
            return Results.Ok(new { ok = true });
        });

        // ---- replay the winning attack against the defense ---------------
        api.MapPost("/{id:int}/replay", async (
                int id, HttpContext http, LevelEngine engine, CancellationToken ct) =>
            {
                var userId = UserId(http);
                if (userId == null) return Unauthorized();

                var outcome = await engine.ReplayAsync(userId, id, ct);
                if (!outcome.Success) return Error(outcome.ErrorCode, outcome.Message);

                return Results.Ok(new
                {
                    blocked = outcome.Blocked,
                    reply = outcome.Reply,
                    systemNote = outcome.SystemNote,
                    defensePoints = outcome.DefensePoints,
                    status = outcome.Status,
                    guidance = outcome.Guidance
                });
            })
            .RequireRateLimiting("chat");
    }

    // The user always comes from the login cookie, never from the request body.
    private static string? UserId(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static IResult Unauthorized() =>
        Results.Json(new ApiError("unauthorized", "Please log in."), statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Error(string? code, string? message) =>
        Results.Json(new ApiError(code ?? "error", message ?? "Something went wrong."), statusCode: StatusFor(code));

    private static int StatusFor(string? code) => code switch
    {
        "message_empty" or "message_too_long" or "flag_empty" => StatusCodes.Status400BadRequest,
        "level_not_found" => StatusCodes.Status404NotFound,
        "daily_cap_user" or "daily_cap_global" => StatusCodes.Status429TooManyRequests,
        "not_captured" or "no_winning_prompt" or "no_defense_for_level"
            or "hints_exhausted" or "already_captured" => StatusCodes.Status409Conflict,
        "llm_quota" or "safety_blocked" or "llm_unavailable" => StatusCodes.Status502BadGateway,
        _ => StatusCodes.Status500InternalServerError
    };
}