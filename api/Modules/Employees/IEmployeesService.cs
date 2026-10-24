using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Domain.Employees;
using Wintech.Hrms.Api.Modules.Profile;

namespace Wintech.Hrms.Api.Modules.Employees;

/// <summary>Employees & organization endpoints (contract §6, FR-EMP-024 / FR-ADM-020..027).</summary>
public interface IEmployeesService : IAppService
{
    Task<PagedResult<DirectoryItemDto>> GetDirectoryAsync(string? q, Guid? departmentId, Guid? locationId,
        string? sort, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>Own record or any record with employee.view; sensitive fields masked unless permitted + unmask.</summary>
    Task<EmployeeProfileDto> GetDetailAsync(Guid id, bool unmask, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmploymentHistoryItemDto>> GetHistoryAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PagedResult<EmployeeListItem>> GetListAsync(string? q, EmploymentStatus? status,
        Guid? departmentId, Guid? locationId, PageRequest page, CancellationToken cancellationToken = default);

    Task<EmployeeProfileDto> CreateAsync(CreateEmployeeRequest request, CancellationToken cancellationToken = default);

    Task<EmployeeProfileDto> UpdateAsync(Guid id, UpdateEmployeeRequest request, CancellationToken cancellationToken = default);

    Task<EmployeeProfileDto> ChangeStatusAsync(Guid id, ChangeEmployeeStatusRequest request,
        CancellationToken cancellationToken = default);
}
