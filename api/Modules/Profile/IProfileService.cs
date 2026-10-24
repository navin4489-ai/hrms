using Microsoft.AspNetCore.Http;
using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Modules.Profile;

/// <summary>
/// Self-service "Me" surface (contract §5, FR-EMP-010..025). Every call is scoped to the
/// caller's <c>ICurrentUser.EmployeeId</c> — there is no way to reach another employee's record.
/// </summary>
public interface IProfileService : IAppService
{
    Task<EmployeeProfileDto> GetMeAsync(CancellationToken cancellationToken = default);

    Task<ProfileSavedResponse> UpdateProfileAsync(UpdateProfileRequest request, CancellationToken cancellationToken = default);

    Task<SensitiveChangeResponse> RequestSensitiveChangeAsync(SensitiveChangeRequest request, CancellationToken cancellationToken = default);

    Task<AvatarResponse> SaveAvatarAsync(IFormFile file, CancellationToken cancellationToken = default);

    /// <summary>Returns the stored avatar stream (404 when the caller has no photo).</summary>
    Task<AvatarStreamResult> OpenAvatarAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmploymentHistoryItemDto>> GetEmploymentHistoryAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NotificationPreferenceDto>> GetNotificationPreferencesAsync(CancellationToken cancellationToken = default);

    Task UpdateNotificationPreferencesAsync(UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, object?>> GetDashboardAsync(CancellationToken cancellationToken = default);
}

/// <summary>Raw avatar bytes produced by the private file store.</summary>
public sealed record AvatarStreamResult(Stream Content, string ContentType);
