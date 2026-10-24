using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Common.Files;
using Wintech.Hrms.Api.Common.Paging;

namespace Wintech.Hrms.Api.Modules.Expenses;

/// <summary>Expenses &amp; travel (contract section 12, FR-EMP-080..084).</summary>
[ApiController]
[Route("api/v1/expenses")]
[Produces("application/json")]
public sealed class ExpensesController : ControllerBase
{
    private readonly IExpenseService _expenses;

    public ExpensesController(IExpenseService expenses) => _expenses = expenses;

    /// <summary>Submit an expense claim with its receipt; creates the manager+finance approval (FR-EMP-080/081).</summary>
    [HttpPost]
    [Authorize]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [ProducesResponseType(typeof(ExpenseCreatedResult), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExpenseCreatedResult>> Create([FromForm] CreateExpenseForm form,
        CancellationToken cancellationToken)
    {
        byte[] receiptBytes;
        if (form.Receipt is { Length: > 0 })
        {
            await using var buffer = new MemoryStream();
            await form.Receipt.CopyToAsync(buffer, cancellationToken);
            receiptBytes = buffer.ToArray();
        }
        else
        {
            receiptBytes = [];
        }

        var result = await _expenses.CreateClaimAsync(new CreateExpenseInput(
            form.Category, form.Amount, form.Currency ?? "INR", form.ExpenseDate,
            form.BusinessPurpose, form.CostCentre, receiptBytes,
            form.Receipt?.FileName ?? string.Empty), cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>List claims — own by default, all with expense.approve/expense.finance; team=true limits to direct reports.</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PagedResult<ExpenseListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ExpenseListItem>>> List(
        [FromQuery] ExpenseQuery query, [FromQuery] PageRequest page, CancellationToken cancellationToken)
        => Ok(await _expenses.ListClaimsAsync(query, page, cancellationToken));

    /// <summary>Claim detail (own or privileged; other people's claims are 404).</summary>
    [HttpGet("{id:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ExpenseListItem), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExpenseListItem>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _expenses.GetClaimAsync(id, cancellationToken));

    /// <summary>Download the private receipt (BR-010: audited, owner/privileged only).</summary>
    [HttpGet("{id:guid}/receipt")]
    [Authorize]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Receipt(Guid id, CancellationToken cancellationToken)
    {
        StoredFileStream file = await _expenses.OpenReceiptAsync(id, cancellationToken);
        return File(file.Content, file.ContentType, file.OriginalFileName);
    }

    /// <summary>Cancel a claim that is still awaiting approval (FR-EMP-082).</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize]
    [ProducesResponseType(typeof(ExpenseListItem), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ExpenseListItem>> Cancel(Guid id, [FromBody] CancelExpenseRequest request,
        CancellationToken cancellationToken)
        => Ok(await _expenses.CancelClaimAsync(id, request.Reason, cancellationToken));
}

/// <summary>Travel requests (contract section 12, FR-EMP-083).</summary>
[ApiController]
[Route("api/v1/travel-requests")]
[Produces("application/json")]
public sealed class TravelRequestsController : ControllerBase
{
    private readonly IExpenseService _expenses;

    public TravelRequestsController(IExpenseService expenses) => _expenses = expenses;

    /// <summary>Submit a travel request; approval-backed (manager then finance).</summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(TravelCreatedResult), StatusCodes.Status201Created)]
    public async Task<ActionResult<TravelCreatedResult>> Create([FromBody] CreateTravelRequestForm form,
        CancellationToken cancellationToken)
    {
        var result = await _expenses.CreateTravelRequestAsync(new CreateTravelRequestInput(
            form.Destination, form.Purpose, form.StartDate, form.EndDate,
            form.EstimatedCost, form.Currency ?? "INR"), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Own travel requests; privileged approvers see all.</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(PagedResult<TravelListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TravelListItem>>> List([FromQuery] PageRequest page,
        CancellationToken cancellationToken)
        => Ok(await _expenses.ListTravelRequestsAsync(page, cancellationToken));
}
