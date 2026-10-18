using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using SqlBackup.Website.Options;

namespace SqlBackup.Website.Services;

/// <summary>
/// Limits how many contact messages one client can send per window. Only messages that are
/// about to be mailed take a permit, so a visitor fixing validation errors is never locked out.
/// </summary>
public sealed class ContactSendLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public ContactSendLimiter(IOptions<ContactFormOptions> options)
    {
        var form = options.Value;
        _limiter = PartitionedRateLimiter.Create<string, string>(client =>
            RateLimitPartition.GetFixedWindowLimiter(client, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, form.PermitLimit),
                Window = TimeSpan.FromMinutes(Math.Max(1, form.WindowMinutes)),
                QueueLimit = 0,
            }));
    }

    /// <summary>Takes a permit for this client; false when it has sent too many messages recently.</summary>
    public bool TryAcquire(string client)
    {
        using var lease = _limiter.AttemptAcquire(client);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
