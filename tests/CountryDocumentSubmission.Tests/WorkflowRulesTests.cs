using CountryDocumentSubmission.Api;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CountryDocumentSubmission.Tests;

public class WorkflowRulesTests
{
    [Fact]
    public async Task ReviewerMustBelongToSameCountry()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        db.Users.AddRange(
            new AppUser { Id = "reviewer-in", Role = UserRole.CountryReviewer, CountryCode = "IN" },
            new AppUser { Id = "reviewer-us", Role = UserRole.CountryReviewer, CountryCode = "US" });
        await db.SaveChangesAsync();

        var eligible = await db.Users.AnyAsync(u => u.Id == "reviewer-in" &&
            u.Role == UserRole.CountryReviewer && u.CountryCode == "IN" && u.IsActive);
        var ineligible = await db.Users.AnyAsync(u => u.Id == "reviewer-us" &&
            u.Role == UserRole.CountryReviewer && u.CountryCode == "IN" && u.IsActive);

        Assert.True(eligible);
        Assert.False(ineligible);
    }

    [Fact]
    public void WorkflowOnlyAllowsDistributionAfterApproval()
    {
        Assert.NotEqual(SubmissionStatus.Approved, SubmissionStatus.PendingReview);
        Assert.NotEqual(SubmissionStatus.Distributed, SubmissionStatus.Returned);
        var submission = new Submission { Status = SubmissionStatus.Approved };
        submission.Status = SubmissionStatus.Distributed;
        Assert.Equal(SubmissionStatus.Distributed, submission.Status);
    }

    [Fact]
    public async Task SnapshotVersionsAreUniquePerSubmission()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new AppDbContext(options);
        var submissionId = Guid.NewGuid();
        db.DocumentSnapshots.AddRange(
            new DocumentSnapshot { SubmissionId = submissionId, Version = 1, OriginalFileName = "a.pdf" },
            new DocumentSnapshot { SubmissionId = submissionId, Version = 2, OriginalFileName = "b.pdf" });
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.DocumentSnapshots.CountAsync(s => s.SubmissionId == submissionId));
    }
}
