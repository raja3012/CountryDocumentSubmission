using System.Security.Cryptography;
using CountryDocumentSubmission.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=country-submissions.db"));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("UserId", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "X-User-Id",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter user ID: editor-in or reviewer-in"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "UserId"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpsRedirection();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (!await db.Users.AnyAsync())
    {
        db.Users.AddRange(
            new AppUser { Id = "editor-in", DisplayName = "India Editor", Role = UserRole.CountryEditor, CountryCode = "IN" },
            new AppUser { Id = "reviewer-in", DisplayName = "India Reviewer", Role = UserRole.CountryReviewer, CountryCode = "IN" },
            new AppUser { Id = "reviewer-us", DisplayName = "US Reviewer", Role = UserRole.CountryReviewer, CountryCode = "US" },
            new AppUser { Id = "editor-us", DisplayName = "US Editor", Role = UserRole.CountryEditor, CountryCode = "US" });
        await db.SaveChangesAsync();
    }
}

var uploadsRoot = Path.Combine(app.Environment.ContentRootPath, "App_Data", "uploads");
Directory.CreateDirectory(uploadsRoot);

app.MapGet("/", () => Results.Ok(new { service = "Country Document Submission API", docs = "/swagger" }));

app.MapGet("/api/users/reviewers", async (HttpContext http, AppDbContext db) =>
{
    var actor = await UserContext.ResolveAsync(http, db);
    if (actor is null) return Results.Unauthorized();
    if (actor.Role != UserRole.CountryEditor) return Results.Forbid();
    var reviewers = await db.Users.Where(u => u.Role == UserRole.CountryReviewer &&
        u.IsActive && u.CountryCode == actor.CountryCode)
        .Select(u => new { u.Id, u.DisplayName, u.CountryCode }).ToListAsync();
    return Results.Ok(reviewers);
});

app.MapPost("/api/submissions", async (CreateSubmissionRequest request, HttpContext http, AppDbContext db) =>
{
    var actor = await UserContext.ResolveAsync(http, db);
    if (actor is null) return Results.Unauthorized();
    if (actor.Role != UserRole.CountryEditor) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(request.Title)) return Results.BadRequest("Title is required.");

    var item = new Submission { Title = request.Title.Trim(), CountryCode = actor.CountryCode, CreatedByUserId = actor.Id };
    item.AuditEntries.Add(new AuditEntry { Action = AuditAction.Created, ActorUserId = actor.Id, Details = "Submission created." });
    db.Submissions.Add(item);
    await db.SaveChangesAsync();
    return Results.Created($"/api/submissions/{item.Id}", new { item.Id, item.Title, item.Status, item.CountryCode });
});

app.MapPost("/api/submissions/{id:guid}/document", async (Guid id, IFormFile file, HttpContext http, AppDbContext db) =>
{
    var actor = await UserContext.ResolveAsync(http, db);
    if (actor is null) return Results.Unauthorized();
    if (actor.Role != UserRole.CountryEditor) return Results.Forbid();
    if (file is null || file.Length == 0) return Results.BadRequest("A non-empty document is required.");
    if (file.Length > 10 * 1024 * 1024) return Results.BadRequest("Maximum file size is 10 MB.");

    var item = await db.Submissions.Include(s => s.Snapshots).FirstOrDefaultAsync(s => s.Id == id);
    if (item is null) return Results.NotFound();
    if (item.CreatedByUserId != actor.Id || item.CountryCode != actor.CountryCode) return Results.Forbid();
    if (item.Status is not (SubmissionStatus.Draft or SubmissionStatus.Returned))
        return Results.Conflict("Documents can only be uploaded to Draft or Returned submissions.");

    var version = item.Snapshots.Count + 1;
    var storedName = $"{id:N}_v{version}_{Guid.NewGuid():N}.bin";
    var path = Path.Combine(uploadsRoot, storedName);
    await using (var stream = File.Create(path))
        await file.CopyToAsync(stream);

    string hash;
    await using (var read = File.OpenRead(path))
        hash = Convert.ToHexString(await SHA256.HashDataAsync(read));

    var snapshot = new DocumentSnapshot {
        SubmissionId = id, Version = version, OriginalFileName = Path.GetFileName(file.FileName),
        StoredFileName = storedName, ContentType = file.ContentType, SizeBytes = file.Length,
        Sha256 = hash, UploadedByUserId = actor.Id, IsSubmittedSnapshot = false
    };
    db.DocumentSnapshots.Add(snapshot);
    item.CurrentVersion = version;
    item.UpdatedAtUtc = DateTime.UtcNow;
    item.AuditEntries.Add(new AuditEntry { Action = AuditAction.DocumentUploaded, ActorUserId = actor.Id,
        Details = $"Document version {version} uploaded (SHA-256 {hash})." });
    await db.SaveChangesAsync();
    return Results.Ok(new { snapshot.Id, snapshot.Version, snapshot.OriginalFileName, snapshot.SizeBytes, snapshot.Sha256 });
}).DisableAntiforgery();

app.MapPost("/api/submissions/{id:guid}/submit", async (Guid id, SubmitRequest request, HttpContext http, AppDbContext db) =>
{
    var actor = await UserContext.ResolveAsync(http, db);
    if (actor is null) return Results.Unauthorized();
    if (actor.Role != UserRole.CountryEditor) return Results.Forbid();

    var item = await db.Submissions.Include(s => s.Snapshots).FirstOrDefaultAsync(s => s.Id == id);
    if (item is null) return Results.NotFound();
    if (item.CreatedByUserId != actor.Id || item.CountryCode != actor.CountryCode) return Results.Forbid();
    if (item.Status is not (SubmissionStatus.Draft or SubmissionStatus.Returned))
        return Results.Conflict("Only Draft or Returned submissions can be submitted.");
    var latest = item.Snapshots.OrderByDescending(s => s.Version).FirstOrDefault();
    if (latest is null) return Results.Conflict("Upload a document before submitting.");
    var reviewer = await db.Users.FirstOrDefaultAsync(u => u.Id == request.ReviewerId &&
        u.Role == UserRole.CountryReviewer && u.IsActive && u.CountryCode == item.CountryCode);
    if (reviewer is null) return Results.BadRequest("Reviewer is not eligible for this country.");
    latest.IsSubmittedSnapshot = true;
    item.AssignedReviewerId = reviewer.Id;
    item.Status = SubmissionStatus.PendingReview;
    item.UpdatedAtUtc = DateTime.UtcNow;
    item.AuditEntries.Add(new AuditEntry { Action = AuditAction.SubmittedForReview, ActorUserId = actor.Id,
        Details = $"Version {latest.Version} submitted to reviewer {reviewer.Id}." });
    await db.SaveChangesAsync();
    return Results.Ok(new { item.Id, item.Status, reviewerId = reviewer.Id, snapshotId = latest.Id, latest.Version });
});

app.MapPost("/api/submissions/{id:guid}/approve", async (Guid id, HttpContext http, AppDbContext db) =>
{
    var actor = await UserContext.ResolveAsync(http, db);
    if (actor is null) return Results.Unauthorized();
    if (actor.Role != UserRole.CountryReviewer) return Results.Forbid();
    var item = await db.Submissions.Include(s => s.Snapshots).FirstOrDefaultAsync(s => s.Id == id);
    if (item is null) return Results.NotFound();
    if (item.CountryCode != actor.CountryCode || item.AssignedReviewerId != actor.Id) return Results.Forbid();
    if (item.Status != SubmissionStatus.PendingReview) return Results.Conflict("Submission is not awaiting review.");
    var snapshot = item.Snapshots.Where(s => s.IsSubmittedSnapshot).OrderByDescending(s => s.Version).First();
    item.ApprovedSnapshotId = snapshot.Id;
    item.Status = SubmissionStatus.Approved;
    item.UpdatedAtUtc = DateTime.UtcNow;
    item.AuditEntries.Add(new AuditEntry { Action = AuditAction.Approved, ActorUserId = actor.Id,
        Details = $"Approved document version {snapshot.Version} (snapshot {snapshot.Id})." });
    await db.SaveChangesAsync();
    return Results.Ok(new { item.Id, item.Status, item.ApprovedSnapshotId });
});

app.MapPost("/api/submissions/{id:guid}/return", async (Guid id, ReturnRequest request, HttpContext http, AppDbContext db) =>
{
    var actor = await UserContext.ResolveAsync(http, db);
    if (actor is null) return Results.Unauthorized();
    if (actor.Role != UserRole.CountryReviewer) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(request.Comments)) return Results.BadRequest("Return comments are required.");
    var item = await db.Submissions.FirstOrDefaultAsync(s => s.Id == id);
    if (item is null) return Results.NotFound();
    if (item.CountryCode != actor.CountryCode || item.AssignedReviewerId != actor.Id) return Results.Forbid();
    if (item.Status != SubmissionStatus.PendingReview) return Results.Conflict("Submission is not awaiting review.");
    item.Status = SubmissionStatus.Returned;
    item.UpdatedAtUtc = DateTime.UtcNow;
    item.AuditEntries.Add(new AuditEntry { Action = AuditAction.Returned, ActorUserId = actor.Id,
        Details = request.Comments.Trim() });
    await db.SaveChangesAsync();
    return Results.Ok(new { item.Id, item.Status });
});

app.MapPost("/api/submissions/{id:guid}/distribute", async (Guid id, DistributeRequest request, HttpContext http, AppDbContext db) =>
{
    var actor = await UserContext.ResolveAsync(http, db);
    if (actor is null) return Results.Unauthorized();
    if (actor.Role != UserRole.CountryEditor) return Results.Forbid();
    if (string.IsNullOrWhiteSpace(request.Recipient)) return Results.BadRequest("Recipient is required.");
    var item = await db.Submissions.FirstOrDefaultAsync(s => s.Id == id);
    if (item is null) return Results.NotFound();
    if (item.CreatedByUserId != actor.Id || item.CountryCode != actor.CountryCode) return Results.Forbid();
    if (item.Status != SubmissionStatus.Approved) return Results.Conflict("Only approved submissions can be distributed.");
    item.DistributionRecipient = request.Recipient.Trim();
    item.Status = SubmissionStatus.Distributed;
    item.UpdatedAtUtc = DateTime.UtcNow;
    item.AuditEntries.Add(new AuditEntry { Action = AuditAction.Distributed, ActorUserId = actor.Id,
        Details = $"Distributed to {item.DistributionRecipient}." });
    await db.SaveChangesAsync();
    return Results.Ok(new { item.Id, item.Status, item.DistributionRecipient });
});

app.MapGet("/api/submissions/{id:guid}", async (Guid id, HttpContext http, AppDbContext db) =>
{
    var actor = await UserContext.ResolveAsync(http, db);
    if (actor is null) return Results.Unauthorized();
    var item = await db.Submissions.Include(s => s.Snapshots).FirstOrDefaultAsync(s => s.Id == id);
    if (item is null) return Results.NotFound();
    if (item.CountryCode != actor.CountryCode) return Results.Forbid();
    if (actor.Role == UserRole.CountryEditor && item.CreatedByUserId != actor.Id) return Results.Forbid();
    if (actor.Role == UserRole.CountryReviewer && item.AssignedReviewerId != actor.Id) return Results.Forbid();
    return Results.Ok(new { item.Id, item.Title, item.CountryCode, item.Status, item.AssignedReviewerId,
        item.CurrentVersion, item.ApprovedSnapshotId, item.DistributionRecipient, item.CreatedAtUtc,
        snapshots = item.Snapshots.OrderBy(s => s.Version).Select(s => new { s.Id, s.Version, s.OriginalFileName, s.SizeBytes, s.Sha256, s.UploadedAtUtc, s.IsSubmittedSnapshot }) });
});

app.MapGet("/api/submissions/{id:guid}/audit", async (Guid id, HttpContext http, AppDbContext db) =>
{
    var actor = await UserContext.ResolveAsync(http, db);
    if (actor is null) return Results.Unauthorized();
    var item = await db.Submissions.FirstOrDefaultAsync(s => s.Id == id);
    if (item is null) return Results.NotFound();
    if (item.CountryCode != actor.CountryCode) return Results.Forbid();
    if (actor.Role == UserRole.CountryEditor && item.CreatedByUserId != actor.Id) return Results.Forbid();
    if (actor.Role == UserRole.CountryReviewer && item.AssignedReviewerId != actor.Id) return Results.Forbid();
    var entries = await db.AuditEntries.Where(a => a.SubmissionId == id).OrderBy(a => a.OccurredAtUtc)
        .Select(a => new { a.Id, a.Action, a.ActorUserId, a.Details, a.OccurredAtUtc }).ToListAsync();
    return Results.Ok(entries);
});

app.MapGet("/api/submissions/{id:guid}/snapshots/{snapshotId:guid}/download", async (Guid id, Guid snapshotId, HttpContext http, AppDbContext db) =>
{
    var actor = await UserContext.ResolveAsync(http, db);
    if (actor is null) return Results.Unauthorized();
    var item = await db.Submissions.FirstOrDefaultAsync(s => s.Id == id);
    if (item is null) return Results.NotFound();
    if (item.CountryCode != actor.CountryCode ||
        (actor.Role == UserRole.CountryEditor && item.CreatedByUserId != actor.Id) ||
        (actor.Role == UserRole.CountryReviewer && item.AssignedReviewerId != actor.Id)) return Results.Forbid();
    var snapshot = await db.DocumentSnapshots.FirstOrDefaultAsync(s => s.Id == snapshotId && s.SubmissionId == id);
    if (snapshot is null) return Results.NotFound();
    var path = Path.Combine(uploadsRoot, snapshot.StoredFileName);
    if (!File.Exists(path)) return Results.NotFound("Stored document is missing.");
    return Results.File(path, snapshot.ContentType, snapshot.OriginalFileName);
});

app.Run();

public partial class Program { }
