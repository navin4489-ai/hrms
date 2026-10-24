using Wintech.Hrms.Api.Common.Paging;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Domain.Cases;

namespace Wintech.Hrms.Api.Modules.Cases;

public interface ICaseService : IAppService
{
    Task<IReadOnlyList<CaseCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<CaseListItem>> ListAsync(PageRequest page, CaseStatus? status, string? category,
        CasePriority? priority, bool mine, string? queue, CancellationToken cancellationToken = default);

    Task<CaseCreatedResult> CreateAsync(CreateCaseRequest request, CancellationToken cancellationToken = default);

    Task<CaseDetailDto> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CaseMessageDto> AddMessageAsync(Guid id, AddCaseMessageRequest request,
        CancellationToken cancellationToken = default);

    Task<CaseMessageDto> AddNoteAsync(Guid id, AddCaseNoteRequest request,
        CancellationToken cancellationToken = default);

    Task<CaseDetailDto> TransitionAsync(Guid id, CaseTransitionRequest request,
        CancellationToken cancellationToken = default);

    Task<CaseDetailDto> RateAsync(Guid id, RateCaseRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeArticleListItem>> SearchKnowledgeBaseAsync(string? q, string? category,
        CancellationToken cancellationToken = default);

    Task<KnowledgeArticleDetail> GetKnowledgeArticleAsync(Guid id, CancellationToken cancellationToken = default);
}
