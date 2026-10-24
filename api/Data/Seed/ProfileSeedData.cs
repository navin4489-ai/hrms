using Microsoft.EntityFrameworkCore;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Domain.Profile;

namespace Wintech.Hrms.Api.Data.Seed;

/// <summary>
/// Idempotent default notification preferences (FR-EMP-121): one row per user x canonical category,
/// created on demand so every account always has a complete preference set.
/// </summary>
public sealed class ProfileSeedData : IDataSeeder
{
    private readonly AppDbContext _db;
    private readonly ILogger<ProfileSeedData> _logger;

    public ProfileSeedData(AppDbContext db, ILogger<ProfileSeedData> logger)
    {
        _db = db;
        _logger = logger;
    }

    public int Order => 105;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var userIds = await _db.UserAccounts.AsNoTracking().Select(u => u.Id).ToListAsync(cancellationToken);
        var created = 0;

        foreach (var userId in userIds)
        {
            var hasAny = await _db.Set<NotificationPreference>()
                .AsNoTracking()
                .AnyAsync(p => p.UserId == userId, cancellationToken);
            if (hasAny) continue;

            foreach (var (category, mandatory) in NotificationCategories.Catalog)
            {
                _db.Set<NotificationPreference>().Add(new NotificationPreference
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Category = category,
                    InApp = true,
                    Push = true,
                    Email = mandatory,
                    Mandatory = mandatory,
                    UpdatedAt = DateTime.UtcNow
                });
                created++;
            }
        }

        if (created > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded {Count} default notification preference rows.", created);
        }
    }
}
