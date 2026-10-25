using Wintech.Hrms.Api.Common.Registration;

namespace Wintech.Hrms.Api.Modules.Attendance;

/// <summary>Shift catalogue, personal roster and swap requests (docs/API_CONTRACT.md §7, FR-EMP-050/051).</summary>
public interface IShiftService : IAppService
{
    Task<IReadOnlyList<ShiftDto>> GetShiftsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminShiftDto>> GetAdminShiftsAsync(CancellationToken cancellationToken = default);

    Task<AdminShiftDto> UpsertShiftAsync(Guid? id, UpsertShiftRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RosterEntryDto>> GetRosterAsync(DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);

    Task<ShiftSwapCreatedDto> CreateSwapRequestAsync(ShiftSwapRequestDto request, CancellationToken cancellationToken = default);
}

/// <summary>Published holidays (FR-ADM-055, FR-EMP-050).</summary>
public interface IHolidayService : IAppService
{
    Task<IReadOnlyList<HolidayDto>> GetHolidaysAsync(int? year, Guid? locationId = null,
        CancellationToken cancellationToken = default);

    Task<HolidayDto> CreateHolidayAsync(UpsertHolidayRequest request, CancellationToken cancellationToken = default);

    Task<HolidayDto> UpdateHolidayAsync(Guid id, UpsertHolidayRequest request, CancellationToken cancellationToken = default);
}
