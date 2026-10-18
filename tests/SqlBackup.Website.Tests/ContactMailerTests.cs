using MailKit.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;
using SqlBackup.Website.Options;
using SqlBackup.Website.Services;

namespace SqlBackup.Website.Tests;

public sealed class ContactMailerTests
{
    private static readonly ContactMessage Message = new(
        "Ada <Lovelace>", "ada@example.com", null, "Bug report",
        "Line one\n<b>not bold</b>", new DateTimeOffset(2026, 10, 18, 9, 30, 0, TimeSpan.Zero), "203.0.113.7", "TestAgent/1.0");

    private static ContactEmailOptions Options(string to = "owner@example.test") => new()
    {
        ToAddress = to,
        FromAddress = "website@example.test",
        FromName = "Website",
        SubjectPrefix = "[Site]",
        Smtp = { Host = "smtp.example.test" },
    };

    [Fact]
    public void Replies_go_to_the_visitor_and_every_recipient_gets_the_message()
    {
        var mime = SmtpContactMailer.BuildMessage(Message, Options("a@example.test; b@example.test, c@example.test"));

        Assert.Equal("website@example.test", mime.From.Mailboxes.Single().Address);
        Assert.Equal(["a@example.test", "b@example.test", "c@example.test"], mime.To.Mailboxes.Select(m => m.Address));
        Assert.Equal("ada@example.com", mime.ReplyTo.Mailboxes.Single().Address);
        Assert.Equal("[Site] Bug report: Ada <Lovelace>", mime.Subject);
    }

    [Fact]
    public void Header_injection_attempts_stay_on_one_line()
    {
        var hostile = Message with { Name = "Mallory\r\nBcc: victim@example.test" };

        var mime = SmtpContactMailer.BuildMessage(hostile, Options());

        Assert.DoesNotContain('\n', mime.Subject ?? "");
        Assert.DoesNotContain('\r', mime.Subject ?? "");
        Assert.Empty(mime.Bcc);
    }

    [Fact]
    public void The_html_body_encodes_visitor_input()
    {
        var mime = SmtpContactMailer.BuildMessage(Message, Options());

        Assert.Contains("&lt;b&gt;not bold&lt;/b&gt;", mime.HtmlBody);
        Assert.Contains("Ada &lt;Lovelace&gt;", mime.HtmlBody);
        Assert.Contains("<b>not bold</b>", mime.TextBody);
        Assert.Contains("IP address: 203.0.113.7", mime.TextBody);
        Assert.Contains("Company: -", mime.TextBody);
    }

    [Theory]
    [InlineData("StartTls", SecureSocketOptions.StartTls)]
    [InlineData("sslonconnect", SecureSocketOptions.SslOnConnect)]
    [InlineData("None", SecureSocketOptions.None)]
    public void Smtp_security_names_are_parsed_case_insensitively(string value, SecureSocketOptions expected) =>
        Assert.Equal(expected, SmtpContactMailer.ParseSecurity(value));

    [Theory]
    [InlineData("SSL")]
    [InlineData("Tls")]
    [InlineData("9")]
    [InlineData("")]
    public void Unknown_smtp_security_values_are_rejected(string value)
    {
        Assert.False(SmtpContactMailer.TryParseSecurity(value, out _));
        Assert.Throws<InvalidOperationException>(() => SmtpContactMailer.ParseSecurity(value));
    }

    [Theory]
    [InlineData("ada@example.com", true)]
    [InlineData("ada.lovelace+backup@sub.example.co.uk", true)]
    [InlineData("Ada <ada@example.com>", false)]
    [InlineData("ada@example,com", false)]
    [InlineData("ada@@example.com", false)]
    public void Address_validation_matches_what_mimekit_can_send_to(string address, bool valid) =>
        Assert.Equal(valid, SmtpContactMailer.IsValidAddress(address));

    [Fact]
    public async Task Pickup_directory_receives_an_eml_file_instead_of_smtp()
    {
        var folder = Path.Combine(Path.GetTempPath(), "sqlbackup-web-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = Options();
            options.Smtp.Host = "";
            options.PickupDirectory = folder;
            var mailer = new SmtpContactMailer(new StaticOptions(options), new TestEnvironment(), NullLogger<SmtpContactMailer>.Instance);

            Assert.True(mailer.IsConfigured);
            await mailer.SendAsync(Message);

            var file = Assert.Single(Directory.GetFiles(folder, "*.eml"));
            var parsed = await MimeMessage.LoadAsync(file);
            Assert.Equal("[Site] Bug report: Ada <Lovelace>", parsed.Subject);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Incomplete_settings_are_reported_as_not_configured()
    {
        var mailer = new SmtpContactMailer(new StaticOptions(new ContactEmailOptions()), new TestEnvironment(), NullLogger<SmtpContactMailer>.Instance);

        Assert.False(mailer.IsConfigured);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mailer.SendAsync(Message));
    }

    private sealed class StaticOptions(ContactEmailOptions value) : IOptionsMonitor<ContactEmailOptions>
    {
        public ContactEmailOptions CurrentValue => value;
        public ContactEmailOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<ContactEmailOptions, string?> listener) => null;
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Production";
    }
}
