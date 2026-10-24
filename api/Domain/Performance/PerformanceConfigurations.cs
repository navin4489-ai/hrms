using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Wintech.Hrms.Api.Domain.Employees;

namespace Wintech.Hrms.Api.Domain.Performance;

public sealed class PerformanceCycleConfiguration : IEntityTypeConfiguration<PerformanceCycle>
{
    public void Configure(EntityTypeBuilder<PerformanceCycle> builder)
    {
        builder.ToTable("pf_performance_cycles");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(c => c.Status);
    }
}

public sealed class PerformanceGoalConfiguration : IEntityTypeConfiguration<PerformanceGoal>
{
    public void Configure(EntityTypeBuilder<PerformanceGoal> builder)
    {
        builder.ToTable("pf_performance_goals");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Title).HasMaxLength(200).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(2000);
        builder.Property(g => g.Category).HasMaxLength(100);
        builder.Property(g => g.Target).HasMaxLength(500);
        builder.Property(g => g.ManagerFeedback).HasMaxLength(2000);
        builder.HasIndex(g => new { g.CycleId, g.EmployeeId });
        builder.HasIndex(g => g.EmployeeId);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(g => g.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SelfAssessmentConfiguration : IEntityTypeConfiguration<SelfAssessment>
{
    public void Configure(EntityTypeBuilder<SelfAssessment> builder)
    {
        builder.ToTable("pf_self_assessments");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Content).IsRequired();
        builder.Property(s => s.Evidence).HasMaxLength(1000);
        // One submission per employee per cycle (FR-EMP-101).
        builder.HasIndex(s => new { s.CycleId, s.EmployeeId }).IsUnique();

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(s => s.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PerformanceReviewConfiguration : IEntityTypeConfiguration<PerformanceReview>
{
    public void Configure(EntityTypeBuilder<PerformanceReview> builder)
    {
        builder.ToTable("pf_performance_reviews");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Summary).HasMaxLength(4000);
        builder.HasIndex(r => new { r.EmployeeId, r.IsPublished });

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class TrainingCourseConfiguration : IEntityTypeConfiguration<TrainingCourse>
{
    public void Configure(EntityTypeBuilder<TrainingCourse> builder)
    {
        builder.ToTable("pf_training_courses");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Title).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(2000);
        builder.Property(c => c.Category).HasMaxLength(100);
        builder.Property(c => c.DurationHours).HasPrecision(6, 1);
        builder.HasIndex(c => c.IsActive);
    }
}

public sealed class TrainingEnrollmentConfiguration : IEntityTypeConfiguration<TrainingEnrollment>
{
    public void Configure(EntityTypeBuilder<TrainingEnrollment> builder)
    {
        builder.ToTable("pf_training_enrollments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.CertificateStorageKey).HasMaxLength(400);
        // One enrolment per employee per course (FR-EMP-104).
        builder.HasIndex(e => new { e.CourseId, e.EmployeeId }).IsUnique();
        builder.HasIndex(e => e.EmployeeId);

        builder.HasOne<Employee>()
            .WithMany()
            .HasForeignKey(e => e.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
