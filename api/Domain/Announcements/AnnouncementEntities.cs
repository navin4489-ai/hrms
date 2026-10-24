namespace Wintech.Hrms.Api.Domain.Announcements;

public enum AnnouncementAudience
{
    All = 0,
    Department = 1,
    Location = 2
}

public enum AnnouncementCategory
{
    Company = 0,
    HR = 1,
    Policy = 2,
    Event = 3,
    IT = 4,
    Wellness = 5
}

public enum AnnouncementStatus
{
    Draft = 0,
    Published = 1,
    Retired = 2
}

/// <summary>Company announcement (FR-ADM-130..132): drafted, published and optionally acknowledged.</summary>
public class Announcement
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public AnnouncementCategory Category { get; set; } = AnnouncementCategory.Company;
    public AnnouncementAudience Audience { get; set; } = AnnouncementAudience.All;
    public bool Pinned { get; set; }
    public AnnouncementStatus Status { get; set; } = AnnouncementStatus.Draft;
    /// <summary>Scheduled publish moment; the announcement appears once this time passes.</summary>
    public DateTime? PublishAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool RequiresAcknowledgement { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public ICollection<AnnouncementTarget> Targets { get; set; } = [];
    public ICollection<AnnouncementAcknowledgement> Acknowledgements { get; set; } = [];
}

/// <summary>Department/Location ids the announcement is targeted at (FR-ADM-131).</summary>
public class AnnouncementTarget
{
    public Guid Id { get; set; }
    public Guid AnnouncementId { get; set; }
    public Announcement? Announcement { get; set; }
    /// <summary>Department id or Location id depending on the announcement's audience.</summary>
    public Guid TargetId { get; set; }
}

/// <summary>Per-user acknowledgement (FR-ADM-132).</summary>
public class AnnouncementAcknowledgement
{
    public Guid Id { get; set; }
    public Guid AnnouncementId { get; set; }
    public Announcement? Announcement { get; set; }
    public Guid UserId { get; set; }
    public Guid? EmployeeId { get; set; }
    public DateTime AcknowledgedAt { get; set; }
}
