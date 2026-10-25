using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wintech.Hrms.Api.Security.Auth;

namespace Wintech.Hrms.Api.Modules.Performance;

/// <summary>Performance self-service (contract section 14, FR-EMP-100..102).</summary>
[ApiController]
[Route("api/v1/performance")]
[Produces("application/json")]
public sealed class PerformanceController : ControllerBase
{
    private readonly IPerformanceService _performance;

    public PerformanceController(IPerformanceService performance) => _performance = performance;

    /// <summary>The cycle open today (or null when no cycle is running).</summary>
    [HttpGet("cycles/current")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentCycleResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<CurrentCycleResult>> CurrentCycle(CancellationToken cancellationToken)
    {
        var cycle = await _performance.GetCurrentCycleAsync(cancellationToken);
        return cycle is null ? new OkObjectResult(null) : Ok(cycle);
    }

    /// <summary>The caller's goals for a cycle (all cycles when cycleId is omitted).</summary>
    [HttpGet("goals")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<GoalItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<GoalItem>>> Goals([FromQuery] Guid? cycleId,
        CancellationToken cancellationToken)
        => Ok(await _performance.ListGoalsAsync(cycleId, cancellationToken));

    /// <summary>Submit the self-assessment for an open cycle (409 when closed or already submitted — FR-EMP-101).</summary>
    [HttpPost("self-assessment")]
    [Authorize]
    [ProducesResponseType(typeof(SelfAssessmentResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SelfAssessmentResult>> SubmitSelfAssessment([FromBody] SubmitSelfAssessmentForm form,
        CancellationToken cancellationToken)
    {
        var result = await _performance.SubmitSelfAssessmentAsync(form, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>The caller's published reviews (FR-EMP-102).</summary>
    [HttpGet("reviews")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<ReviewItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ReviewItem>>> Reviews(CancellationToken cancellationToken)
        => Ok(await _performance.ListReviewsAsync(cancellationToken));

    /// <summary>Acknowledge receipt of a published review — acknowledgement is not agreement (FR-EMP-102).</summary>
    [HttpPost("reviews/{id:guid}/acknowledge")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcknowledgeReview(Guid id, CancellationToken cancellationToken)
    {
        await _performance.AcknowledgeReviewAsync(id, cancellationToken);
        return NoContent();
    }
}

/// <summary>Training catalogue (contract section 14, FR-EMP-103/104).</summary>
[ApiController]
[Route("api/v1/training/courses")]
[Produces("application/json")]
public sealed class TrainingController : ControllerBase
{
    private readonly ITrainingService _training;

    public TrainingController(ITrainingService training) => _training = training;

    /// <summary>Active courses with the caller's enrolment status; filter by status=NotEnrolled|Enrolled|Completed|Overdue.</summary>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<CourseItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CourseItem>>> List([FromQuery] TrainingCourseQuery query,
        CancellationToken cancellationToken)
        => Ok(await _training.ListCoursesAsync(query, cancellationToken));

    /// <summary>Register for an eligible course (409 already enrolled, 422 not eligible — FR-EMP-104).</summary>
    [HttpPost("{id:guid}/enroll")]
    [Authorize]
    [ProducesResponseType(typeof(EnrollmentResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EnrollmentResult>> Enroll(Guid id, CancellationToken cancellationToken)
    {
        var result = await _training.EnrollAsync(id, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Record completion (optional courses self-service; mandatory needs training.manage — FR-EMP-103).</summary>
    [HttpPost("{id:guid}/complete")]
    [Authorize]
    [ProducesResponseType(typeof(CourseCompletionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CourseCompletionResult>> Complete(Guid id, [FromBody] CompleteCourseForm form,
        CancellationToken cancellationToken)
        => Ok(await _training.CompleteAsync(id, form.Score, cancellationToken));
}

/// <summary>Administrative management for review cycles and training catalogue.</summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize]
[Produces("application/json")]
public sealed class PerformanceAdministrationController : ControllerBase
{
    private readonly IPerformanceService _performance;
    private readonly ITrainingService _training;

    public PerformanceAdministrationController(IPerformanceService performance, ITrainingService training)
    {
        _performance = performance;
        _training = training;
    }

    [HttpGet("performance-cycles")]
    [Permission(Wintech.Hrms.Api.Security.Permissions.PerformanceManage)]
    public async Task<ActionResult<IReadOnlyList<PerformanceCycleAdminDto>>> Cycles(CancellationToken cancellationToken)
        => Ok(await _performance.GetAdminCyclesAsync(cancellationToken));

    [HttpPost("performance-cycles")]
    [Permission(Wintech.Hrms.Api.Security.Permissions.PerformanceManage)]
    [ProducesResponseType(typeof(PerformanceCycleAdminDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<PerformanceCycleAdminDto>> CreateCycle(
        [FromBody] UpsertPerformanceCycleRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created,
            await _performance.UpsertCycleAsync(null, request, cancellationToken));

    [HttpPut("performance-cycles/{id:guid}")]
    [Permission(Wintech.Hrms.Api.Security.Permissions.PerformanceManage)]
    public async Task<ActionResult<PerformanceCycleAdminDto>> UpdateCycle(Guid id,
        [FromBody] UpsertPerformanceCycleRequest request, CancellationToken cancellationToken)
        => Ok(await _performance.UpsertCycleAsync(id, request, cancellationToken));

    [HttpGet("training/courses")]
    [Permission(Wintech.Hrms.Api.Security.Permissions.TrainingManage)]
    public async Task<ActionResult<IReadOnlyList<TrainingCourseAdminDto>>> Courses(CancellationToken cancellationToken)
        => Ok(await _training.GetAdminCoursesAsync(cancellationToken));

    [HttpPost("training/courses")]
    [Permission(Wintech.Hrms.Api.Security.Permissions.TrainingManage)]
    [ProducesResponseType(typeof(TrainingCourseAdminDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<TrainingCourseAdminDto>> CreateCourse(
        [FromBody] UpsertTrainingCourseRequest request, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created,
            await _training.UpsertCourseAsync(null, request, cancellationToken));

    [HttpPut("training/courses/{id:guid}")]
    [Permission(Wintech.Hrms.Api.Security.Permissions.TrainingManage)]
    public async Task<ActionResult<TrainingCourseAdminDto>> UpdateCourse(Guid id,
        [FromBody] UpsertTrainingCourseRequest request, CancellationToken cancellationToken)
        => Ok(await _training.UpsertCourseAsync(id, request, cancellationToken));
}
