using System.Net;
using AngleSharp.Html.Dom;

namespace SqlBackup.Website.Tests;

public sealed class ContactFormTests
{
    private static async Task<(HttpClient Client, Dictionary<string, string> Form)> OpenFormAsync(WebsiteFactory factory)
    {
        var client = factory.CreateNoRedirectClient();
        var doc = await client.GetDocumentAsync("/contact");
        var form = doc.QuerySelector("form[method=post]")!;
        var fields = form.QuerySelectorAll("input[type=hidden]")
            .Cast<IHtmlInputElement>()
            .ToDictionary(i => i.Name!, i => i.Value);
        fields["Input.Name"] = "Ada Lovelace";
        fields["Input.Email"] = "ada@example.com";
        fields["Input.Company"] = "Analytical Engines";
        fields["Input.Topic"] = "Bug report";
        fields["Input.Message"] = "The nightly job fails with OS error 5 on the NAS share.";
        fields["Website"] = "";
        return (client, fields);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Dictionary<string, string> form) =>
        client.PostAsync("/contact", new FormUrlEncodedContent(form));

    [Fact]
    public async Task A_valid_message_is_mailed_and_redirects_to_thank_you()
    {
        using var factory = new WebsiteFactory();
        var (client, form) = await OpenFormAsync(factory);
        factory.Clock.Advance(TimeSpan.FromSeconds(20));

        using var response = await PostAsync(client, form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/contact/thank-you", response.Headers.Location?.OriginalString);
        var sent = Assert.Single(factory.Mailer.Sent);
        Assert.Equal("Ada Lovelace", sent.Name);
        Assert.Equal("ada@example.com", sent.Email);
        Assert.Equal("Analytical Engines", sent.Company);
        Assert.Equal("Bug report", sent.Topic);
        Assert.Contains("OS error 5", sent.Message);

        var thanks = await client.GetDocumentAsync("/contact/thank-you");
        Assert.NotNull(thanks.QuerySelector("[data-ga-pageview-event=generate_lead]"));
    }

    [Fact]
    public async Task Honeypot_submissions_look_successful_but_send_nothing()
    {
        using var factory = new WebsiteFactory();
        var (client, form) = await OpenFormAsync(factory);
        factory.Clock.Advance(TimeSpan.FromSeconds(20));
        form["Website"] = "http://spam.example";

        using var response = await PostAsync(client, form);

        Assert.Equal("/contact/thank-you", response.Headers.Location?.OriginalString);
        Assert.Empty(factory.Mailer.Sent);
    }

    [Fact]
    public async Task Instant_submissions_are_treated_as_bots()
    {
        using var factory = new WebsiteFactory();
        var (client, form) = await OpenFormAsync(factory);
        factory.Clock.Advance(TimeSpan.FromSeconds(1));

        using var response = await PostAsync(client, form);

        Assert.Equal("/contact/thank-you", response.Headers.Location?.OriginalString);
        Assert.Empty(factory.Mailer.Sent);
    }

    [Fact]
    public async Task Expired_or_tampered_forms_ask_the_visitor_to_resend()
    {
        using var factory = new WebsiteFactory();
        var (client, form) = await OpenFormAsync(factory);
        factory.Clock.Advance(TimeSpan.FromHours(13));

        using var expired = await PostAsync(client, form);
        Assert.Equal(HttpStatusCode.OK, expired.StatusCode);
        Assert.Contains("This form has expired", await expired.Content.ReadAsStringAsync());

        form["FormToken"] = "not-a-valid-token";
        using var tampered = await PostAsync(client, form);
        Assert.Contains("This form has expired", await tampered.Content.ReadAsStringAsync());
        Assert.Empty(factory.Mailer.Sent);
    }

    [Fact]
    public async Task Invalid_input_is_reported_next_to_the_fields()
    {
        using var factory = new WebsiteFactory();
        var (client, form) = await OpenFormAsync(factory);
        factory.Clock.Advance(TimeSpan.FromSeconds(20));
        form["Input.Email"] = "not-an-email";
        form["Input.Message"] = "too short";
        form["Input.Topic"] = "Something invented";

        using var response = await PostAsync(client, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = Html.Parse(await response.Content.ReadAsStringAsync());
        var errors = doc.QuerySelectorAll(".field-validation-error").Select(e => e.TextContent.Trim()).Where(t => t.Length > 0).ToList();
        Assert.Contains("Please enter a valid email address.", errors);
        Assert.Contains("Please write between 20 and 5,000 characters.", errors);
        Assert.Contains("Please choose a topic from the list.", errors);
        Assert.Equal("Ada Lovelace", (doc.QuerySelector("#Input_Name") as IHtmlInputElement)?.Value);
        Assert.Empty(factory.Mailer.Sent);
    }

    [Fact]
    public async Task Mail_failures_keep_the_message_and_show_an_error()
    {
        using var factory = new WebsiteFactory();
        factory.Mailer.Failure = new InvalidOperationException("SMTP down");
        var (client, form) = await OpenFormAsync(factory);
        factory.Clock.Advance(TimeSpan.FromSeconds(20));

        using var response = await PostAsync(client, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = Html.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("could not be sent", doc.QuerySelector(".alert--error")?.TextContent);
        Assert.Contains("OS error 5", doc.QuerySelector("#Input_Message")?.TextContent);
    }

    [Fact]
    public async Task Unconfigured_mail_settings_show_an_error_instead_of_failing()
    {
        using var factory = new WebsiteFactory();
        factory.Mailer.IsConfigured = false;
        var (client, form) = await OpenFormAsync(factory);
        factory.Clock.Advance(TimeSpan.FromSeconds(20));

        using var response = await PostAsync(client, form);

        Assert.Contains("not available right now", await response.Content.ReadAsStringAsync());
        Assert.Empty(factory.Mailer.Sent);
    }

    [Fact]
    public async Task Posts_without_an_antiforgery_token_are_rejected()
    {
        using var factory = new WebsiteFactory();
        var (client, form) = await OpenFormAsync(factory);
        factory.Clock.Advance(TimeSpan.FromSeconds(20));
        form.Remove("__RequestVerificationToken");

        using var response = await PostAsync(client, form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Mailer.Sent);
    }

    [Fact]
    public async Task Repeated_posts_are_rate_limited()
    {
        using var factory = new WebsiteFactory(new Dictionary<string, string?> { ["ContactForm:PermitLimit"] = "2" });
        var (client, form) = await OpenFormAsync(factory);
        factory.Clock.Advance(TimeSpan.FromSeconds(20));

        (await PostAsync(client, form)).Dispose();
        (await PostAsync(client, form)).Dispose();
        using var limited = await PostAsync(client, form);

        Assert.Equal(HttpStatusCode.SeeOther, limited.StatusCode);
        Assert.Equal("/contact?limited=true", limited.Headers.Location?.OriginalString);
        Assert.Equal(2, factory.Mailer.Sent.Count);
        var page = await client.GetStringAsync("/contact?limited=true");
        Assert.Contains("sent several messages in a short time", page);
    }
}
