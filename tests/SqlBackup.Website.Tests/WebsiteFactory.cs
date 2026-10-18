using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SqlBackup.Website.Services;

namespace SqlBackup.Website.Tests;

/// <summary>Runs the real website in memory with test settings, a fake mailer and a controllable clock.</summary>
public sealed class WebsiteFactory : WebApplicationFactory<Program>
{
    public const string BaseUrl = "https://www.example.test";

    private readonly Dictionary<string, string?> _settings = new()
    {
        ["Site:BaseUrl"] = BaseUrl,
        ["Site:AllowIndexing"] = "true",
        ["GoogleAnalytics:MeasurementId"] = "",
        ["GoogleSearchConsole:VerificationToken"] = "",
        ["GoogleSearchConsole:HtmlVerificationFile"] = "",
        ["ContactEmail:ToAddress"] = "owner@example.test",
        ["ContactEmail:FromAddress"] = "website@example.test",
        ["ContactEmail:Smtp:Host"] = "smtp.example.test",
        ["ContactEmail:PickupDirectory"] = "",
        ["ContactForm:PermitLimit"] = "1000",
    };

    public WebsiteFactory()
        : this(null)
    {
    }

    internal WebsiteFactory(IDictionary<string, string?>? overrides)
    {
        foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
            _settings[key] = value;
    }

    public FakeMailer Mailer { get; } = new();

    public ManualClock Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IContactMailer>();
            services.AddSingleton<IContactMailer>(Mailer);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    public HttpClient CreateNoRedirectClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
}

public sealed class FakeMailer : IContactMailer
{
    public List<ContactMessage> Sent { get; } = new();

    public bool IsConfigured { get; set; } = true;

    public Exception? Failure { get; set; }

    public Task SendAsync(ContactMessage message, CancellationToken ct = default)
    {
        if (Failure is not null)
            throw Failure;
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
