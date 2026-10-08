using BreakTheBot.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BreakTheBot.Web.Services;

public record LimitCheck(bool Allowed, string? ErrorCode = null, string? Message = null);

public record RemainingQuota(int UserRemainingToday, int GlobalRemainingToday);

/// <summary>Protects the shared free Gemini quota: message size, per-user and global daily caps.</summary>
public class UsageLimiter
{
    private readonly AppDbContext _db;
    private readonly int _perDayPerUser;
    private readonly int _perDayGlobal;
    private readonly int _maxMessageChars;

    public UsageLimiter(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _perDayPerUser = config.GetValue("Limits:PerDayPerUser", 40);
        _perDayGlobal = config.GetValue("Limits:PerDayGlobal", 400);
        _maxMessageChars = config.GetValue("Limits:MaxMessageChars", 4000);
    }

    public int MaxMessageChars => _maxMessageChars;

    public async Task<LimitCheck> CheckAsync(string userId, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return new LimitCheck(false, "message_empty", "Type a message first.");

        if (message.Length > _maxMessageChars)
            return new LimitCheck(false, "message_too_long",
                $"That message is too long. The limit is {_maxMessageChars} characters.");

        var start = DateTime.UtcNow.Date;

        var userCount = await _db.ChatLogs.CountAsync(c => c.UserId == userId && c.CreatedUtc >= start);
        if (userCount >= _perDayPerUser)
            return new LimitCheck(false, "daily_cap_user",
                $"You have used your {_perDayPerUser} messages for today. Your allowance resets at midnight UTC.");

        var globalCount = await _db.ChatLogs.CountAsync(c => c.CreatedUtc >= start);
        if (globalCount >= _perDayGlobal)
            return new LimitCheck(false, "daily_cap_global",
                "The lab has reached its free AI limit for today. Please come back tomorrow.");

        return new LimitCheck(true);
    }

    public async Task<RemainingQuota> GetRemainingAsync(string userId)
    {
        var start = DateTime.UtcNow.Date;
        var userCount = await _db.ChatLogs.CountAsync(c => c.UserId == userId && c.CreatedUtc >= start);
        var globalCount = await _db.ChatLogs.CountAsync(c => c.CreatedUtc >= start);
        return new RemainingQuota(
            Math.Max(0, _perDayPerUser - userCount),
            Math.Max(0, _perDayGlobal - globalCount));
    }
}