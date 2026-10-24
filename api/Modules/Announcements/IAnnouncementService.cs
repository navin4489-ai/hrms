using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Domain.Announcements;

namespace Wintech.Hrms.Api.Modules.Announcements;

public interface IAnnouncementService : IAppService
{
    Task<PagedResult<AnnouncementDto>> ListAsync(AnnouncementCategory? category, PageRequest page,
        CancellationToken cancellationToken = default);

    Task<AnnouncementDto> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    Task AcknowledgeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<AnnouncementDto> CreateAsync(CreateAnnouncementRequest request, CancellationToken cancellationToken = default);

    Task<AnnouncementDto> PublishAsync(Guid id, CancellationToken cancellationToken = default);
}
