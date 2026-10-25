using Wintech.Hrms.Api.Common.Tenancy;

namespace Wintech.Hrms.Api.Domain.Chat;

/// <summary>
/// A push-notification registration (FCM token) for a user's device. Used to deliver message and
/// incoming-call notifications when the app is backgrounded or closed. Org-owned for isolation.
/// </summary>
public class DeviceToken : IOrgOwned
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid EmployeeId { get; set; }

    /// <summary>FCM registration token.</summary>
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = "android";
    public DateTime UpdatedAt { get; set; }
}
