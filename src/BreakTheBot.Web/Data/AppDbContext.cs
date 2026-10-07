using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BreakTheBot.Web.Data;

public class AppDbContext : IdentityDbContext<IdentityUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<LevelProgress> LevelProgress => Set<LevelProgress>();
    public DbSet<ChatLog> ChatLogs => Set<ChatLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<LevelProgress>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.WinningPrompt).HasMaxLength(4000);
            e.HasIndex(x => new { x.UserId, x.LevelId }).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ChatLog>(e =>
        {
            e.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            e.Property(x => x.UserMessage).HasMaxLength(4000).IsRequired();
            e.Property(x => x.BotResponse).HasMaxLength(8000).IsRequired();
            e.HasIndex(x => new { x.UserId, x.CreatedUtc });
            e.HasIndex(x => x.CreatedUtc);
            e.HasIndex(x => new { x.UserId, x.LevelId, x.IsReset, x.CreatedUtc });
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}