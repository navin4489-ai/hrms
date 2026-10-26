using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Wintech.Hrms.Api.Modules.Profile;

/// <summary>Self-service "Me" surface — contract §5 (FR-EMP-010..025). Always scoped to the caller.</summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
[Produces("application/json")]
public sealed class MeController : ControllerBase
{
    private readonly IProfileService _profile;
    private readonly Payroll.IPayrollService _payroll;

    public MeController(IProfileService profile, Payroll.IPayrollService payroll)
    {
        _profile = profile;
        _payroll = payroll;
    }

    /// <summary>Personal + work profile of the signed-in employee (FR-EMP-020).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(EmployeeProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EmployeeProfileDto>> GetMe(CancellationToken cancellationToken)
        => Ok(await _profile.GetMeAsync(cancellationToken));

    /// <summary>
    /// The caller's own pay details (CTC breakup, regime) with the automatically computed
    /// income tax — read-only self-service; 404 until finance records pay details.
    /// </summary>
    [HttpGet("salary")]
    [ProducesResponseType(typeof(Payroll.AdminSalaryStructureDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Payroll.AdminSalaryStructureDto>> MySalary(CancellationToken cancellationToken)
        => Ok(await _payroll.GetMySalaryAsync(cancellationToken));

    /// <summary>Submit changes to the permitted non-sensitive fields (FR-EMP-021).</summary>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(ProfileSavedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProfileSavedResponse>> UpdateProfile(
        [FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
        => Ok(await _profile.UpdateProfileAsync(request, cancellationToken));

    /// <summary>Request a change to a sensitive field; routed into approvals, value masked until verified (FR-EMP-022).</summary>
    [HttpPost("sensitive-change-requests")]
    [ProducesResponseType(typeof(SensitiveChangeResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SensitiveChangeResponse>> RequestSensitiveChange(
        [FromBody] SensitiveChangeRequest request, CancellationToken cancellationToken)
        => Accepted(await _profile.RequestSensitiveChangeAsync(request, cancellationToken));

    /// <summary>Upload a profile photo (jpg/png, <= 2 MB, BR-010).</summary>
    [HttpPost("avatar")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AvatarResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AvatarResponse>> UploadAvatar(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
            throw new Common.ErrorHandling.RequestValidationException(errors: new()
            {
                ["file"] = ["A file is required."]
            });
        return Ok(await _profile.SaveAvatarAsync(file, cancellationToken));
    }

    /// <summary>Download the caller's profile photo (404 when none).</summary>
    [HttpGet("avatar")]
    [Produces("image/jpeg", "image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvatar(CancellationToken cancellationToken)
    {
        var avatar = await _profile.OpenAvatarAsync(cancellationToken);
        return File(avatar.Content, avatar.ContentType);
    }

    /// <summary>Approved employment events — promotions, transfers, designation changes (FR-EMP-025).</summary>
    [HttpGet("employment-history")]
    [ProducesResponseType(typeof(IReadOnlyList<EmploymentHistoryItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<EmploymentHistoryItemDto>>> EmploymentHistory(
        CancellationToken cancellationToken)
        => Ok(await _profile.GetEmploymentHistoryAsync(cancellationToken));

    /// <summary>Per-category channel preferences; mandatory items can never be disabled (FR-EMP-121).</summary>
    [HttpGet("notification-preferences")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationPreferenceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NotificationPreferenceDto>>> NotificationPreferences(
        CancellationToken cancellationToken)
        => Ok(await _profile.GetNotificationPreferencesAsync(cancellationToken));

    /// <summary>Update channel preferences (204; mandatory categories cannot be disabled -> 422).</summary>
    [HttpPut("notification-preferences")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateNotificationPreferences(
        [FromBody] UpdateNotificationPreferencesRequest request, CancellationToken cancellationToken)
    {
        await _profile.UpdateNotificationPreferencesAsync(request, cancellationToken);
        return NoContent();
    }

    /// <summary>Home dashboard: greeting, employee summary, approvals and slices from every module (FR-EMP-010..014).</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(IReadOnlyDictionary<string, object?>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyDictionary<string, object?>>> Dashboard(CancellationToken cancellationToken)
        => Ok(await _profile.GetDashboardAsync(cancellationToken));
}
