using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Modules.Notifications;

public interface INotificationService : IAppService
{
    Task<PagedResult<NotificationDto>> ListAsync(bool unreadOnly, PageRequest page,
        CancellationToken cancellationToken = default);

    Task<UnreadCountResult> UnreadCountAsync(CancellationToken cancellationToken = default);

    Task MarkReadAsync(Guid id, CancellationToken cancellationToken = default);

    Task MarkAllReadAsync(CancellationToken cancellationToken = default);
}
