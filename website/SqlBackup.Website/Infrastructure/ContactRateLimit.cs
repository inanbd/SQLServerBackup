namespace SqlBackup.Website.Infrastructure;

public static class ContactRateLimit
{
    /// <summary>Rate-limiter policy applied to the contact page (counts POSTs only).</summary>
    public const string PolicyName = "contact";
}
