using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Modules.Attendance;

/// <summary>Attendance corrections routed through approvals (docs/API_CONTRACT.md §7, FR-EMP-035..037, FR-ADM-042/044).</summary>
public interface IAttendanceCorrectionService : IAppService
{
    Task<AttendanceCorrectionCreatedDto> CreateAsync(AttendanceCorrectionCreateDto request,
        CancellationToken cancellationToken = default);

    Task<Common.Paging.PagedResult<AttendanceCorrectionListItem>> ListAsync(
        string? status, string? month, Guid? employeeId, Common.Paging.PageRequest page,
        CancellationToken cancellationToken = default);

    Task<AttendanceCorrectionDetail> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);
}
