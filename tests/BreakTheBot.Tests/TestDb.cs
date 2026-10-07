using BreakTheBot.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BreakTheBot.Tests;

/// <summary>A fresh in-memory SQLite database for one test.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;
    public AppDbContext Db { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new AppDbContext(options);
        Db.Database.EnsureCreated();
    }

    /// <summary>Creates a user (progress rows need a real user because of the foreign key).</summary>
    public async Task<string> AddUserAsync(string name = "alex")
    {
        var user = new IdentityUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"{name}@test.com",
            NormalizedUserName = $"{name}@TEST.COM".ToUpperInvariant(),
            Email = $"{name}@test.com",
            NormalizedEmail = $"{name}@TEST.COM".ToUpperInvariant()
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();
        return user.Id;
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}