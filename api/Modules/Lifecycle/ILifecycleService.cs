using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Domain.Approvals;
using Wintech.Hrms.Api.Domain.Lifecycle;

namespace Wintech.Hrms.Api.Modules.Lifecycle;

/// <summary>
/// Lifecycle module: resignations/exit (contract §21) and onboarding/offboarding
/// checklists (contract §22). Auto-registered scoped by <c>AddAppServices</c>.
/// </summary>
public interface ILifecycleService : IAppService
{
    // ---- resignations (own record unless the caller holds employee.manage) ----

    Task<ResignationCreateResponse> CreateResignationAsync(CreateResignationRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResignationListItem>> ListResignationsAsync(ResignationStatus? status,
        CancellationToken cancellationToken = default);

    Task<ResignationDetailDto> GetResignationAsync(Guid id, CancellationToken cancellationToken = default);

    Task<WithdrawResponse> WithdrawResignationAsync(Guid id, string reason, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExitChecklistItemDto>> GetExitChecklistAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ExitChecklistItemDto> CompleteExitItemAsync(Guid id, Guid itemId, CompleteExitItemRequest request,
        CancellationToken cancellationToken = default);

    Task<ExitSurveyDto> SubmitExitSurveyAsync(Guid id, ExitSurveyRequest request, CancellationToken cancellationToken = default);

    // ---- onboarding / offboarding checklists ----

    Task<PagedResult<ChecklistListItem>> ListChecklistsAsync(PageRequest page, LifecycleChecklistType? type,
        string? status, string? q, CancellationToken cancellationToken = default);

    Task<ChecklistDetailDto> CreateChecklistAsync(CreateChecklistRequest request, CancellationToken cancellationToken = default);

    Task<ChecklistDetailDto> GetChecklistAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ChecklistTaskDto> TransitionTaskAsync(Guid id, Guid taskId, LifecycleTaskAction action, string? note,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LifecycleTemplateDto>> ListTemplatesAsync(CancellationToken cancellationToken = default);

    // ---- approval engine callbacks (IApprovalHandler delegates here) ----

    /// <summary>Approved -> create the exit checklist and set the relieving date; otherwise revert the status.</summary>
    Task ApplyApprovalOutcomeAsync(ApprovalTask approvalTask, ApprovalActionType finalAction, string? comment,
        CancellationToken cancellationToken = default);

    /// <summary>Requester (or engine) cancelled a still-pending resignation.</summary>
    Task CancelResignationAsync(ApprovalTask approvalTask, string? reason, CancellationToken cancellationToken = default);
}
