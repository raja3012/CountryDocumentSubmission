namespace CountryDocumentSubmission.Api;

public enum UserRole { CountryEditor, CountryReviewer }
public enum SubmissionStatus { Draft, PendingReview, Returned, Approved, Distributed }
public enum AuditAction { Created, DocumentUploaded, SubmittedForReview, Approved, Returned, Distributed }

public sealed class AppUser
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string DisplayName { get; set; } = "";
    public UserRole Role { get; set; }
    public string CountryCode { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class Submission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string CreatedByUserId { get; set; } = "";
    public string? AssignedReviewerId { get; set; }
    public SubmissionStatus Status { get; set; } = SubmissionStatus.Draft;
    public int CurrentVersion { get; set; }
    public Guid? ApprovedSnapshotId { get; set; }
    public string? DistributionRecipient { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<DocumentSnapshot> Snapshots { get; set; } = [];
    public List<AuditEntry> AuditEntries { get; set; } = [];
}

public sealed class DocumentSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubmissionId { get; set; }
    public int Version { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public string UploadedByUserId { get; set; } = "";
    public bool IsSubmittedSnapshot { get; set; }
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public Guid SubmissionId { get; set; }
    public AuditAction Action { get; set; }
    public string ActorUserId { get; set; } = "";
    public string Details { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record CreateSubmissionRequest(string Title);
public sealed record SubmitRequest(string ReviewerId);
public sealed record ReturnRequest(string Comments);
public sealed record DistributeRequest(string Recipient);
