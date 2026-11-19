namespace CountryDocumentSubmission.Api;

public sealed record CurrentUser(string Id, string DisplayName, UserRole Role, string CountryCode);

public static class UserContext
{
    public static async Task<CurrentUser?> ResolveAsync(HttpContext http, AppDbContext db)
    {
        var id = http.Request.Headers["X-User-Id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id)) return null;
        var user = await db.Users.FindAsync(id);
        if (user is null || !user.IsActive) return null;
        return new CurrentUser(user.Id, user.DisplayName, user.Role, user.CountryCode);
    }
}
