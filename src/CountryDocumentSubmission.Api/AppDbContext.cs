using Microsoft.EntityFrameworkCore;

namespace CountryDocumentSubmission.Api;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<DocumentSnapshot> DocumentSnapshots => Set<DocumentSnapshot>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasKey(x => x.Id);
        modelBuilder.Entity<AppUser>().HasIndex(x => new { x.CountryCode, x.Role });
        modelBuilder.Entity<Submission>().HasKey(x => x.Id);
        modelBuilder.Entity<Submission>().HasMany(x => x.Snapshots).WithOne().HasForeignKey(x => x.SubmissionId);
        modelBuilder.Entity<Submission>().HasMany(x => x.AuditEntries).WithOne().HasForeignKey(x => x.SubmissionId);
        modelBuilder.Entity<DocumentSnapshot>().HasIndex(x => new { x.SubmissionId, x.Version }).IsUnique();
        modelBuilder.Entity<AuditEntry>().HasKey(x => x.Id);
    }
}
