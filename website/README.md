# SQL Server Backup Suite — website

The product website for SQL Server Backup Suite: an ASP.NET Core 8 Razor Pages
(`.cshtml`) site with feature pages, a full user guide, an FAQ, a download page and
a contact form that emails you. It is fast (no CSS/JS framework, system fonts,
WebP screenshots, Brotli), search-engine ready and configured entirely from
`appsettings.json`.

```
website/SqlBackup.Website/        the site
  Pages/                          Razor pages (one .cshtml per URL)
  Seo/                            page metadata, sitemap.xml, robots.txt, JSON-LD
  Services/ContactMailer.cs       contact form email (MailKit)
  Infrastructure/                 security headers (CSP), canonical URLs, screenshot tag helper
  wwwroot/img/screens/            application screenshots, 1x and 2x WebP
tests/SqlBackup.Website.Tests/    integration tests (SEO, Google settings, contact form, links)
```

## Run it locally

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
cd website/SqlBackup.Website
dotnet run                 # http://localhost:5080 (Development)
dotnet test ../../tests/SqlBackup.Website.Tests
```

In Development, contact-form messages are written as `.eml` files to
`App_Data/mail` instead of being sent, and the site tells search engines not to
index it.

## Configuration (`appsettings.json`)

Every setting below lives in `website/SqlBackup.Website/appsettings.json`. For
Google Analytics, Search Console and the contact form, empty values switch the
feature off. Invalid values (a malformed URL, measurement ID, SMTP security mode
or proxy address) stop the site at startup with a message naming the setting.

### `Site`

| Key | Default | Purpose |
|---|---|---|
| `Name` | `SQL Server Backup Suite` | Site / product name in titles and structured data |
| `BaseUrl` | `https://sql-backup.kaizar.us` | **Required.** Public origin, no trailing slash. Used for canonical URLs, `sitemap.xml`, `robots.txt` and Open Graph tags |
| `Owner` | `inanbd` | **Required.** Copyright holder (footer) and author in structured data |
| `OwnerUrl` | GitHub profile | Optional profile URL for the author in structured data |
| `Version` | empty | Version on the download page and in structured data. Empty = the application's build version (`<Version>` in `Directory.Build.props`), so it follows releases automatically |
| `RepositoryUrl`, `IssuesUrl` | GitHub repo / issues | **Required.** Links in navigation, footer and docs |
| `DownloadUrl` | GitHub Releases | **Required.** Target of every *Download* button |
| `TwitterSite` | empty | Optional `@account` for `twitter:site` |
| `AllowIndexing` | `true` | Set `false` on staging: `robots.txt` disallows everything and every response sends `X-Robots-Tag: noindex` |
| `BehindReverseProxy` | `false` | Set `true` behind nginx/a load balancer/a container platform so client IPs (rate limiting) and HTTPS are detected from `X-Forwarded-*` |
| `TrustedProxies` | `[]` | IP addresses or CIDR networks of your proxies, e.g. `["10.0.0.0/8"]`. With `BehindReverseProxy` on and this list empty, `X-Forwarded-*` is believed from any sender — fine only when the app's port is reachable from the proxy alone |

### `DataProtection`

| Key | Purpose |
|---|---|
| `KeysDirectory` | Folder for the ASP.NET Core Data Protection keys that encrypt the contact form's anti-forgery and timing tokens. Leave empty on IIS or a single long-lived server. In containers, point it at a persistent volume (e.g. `/data/keys`) — otherwise every redeploy creates new keys and forms that were open at that moment ask the visitor to send again. Several instances must share the folder |

### `GoogleAnalytics`

| Key | Purpose |
|---|---|
| `MeasurementId` | Your GA4 measurement ID, e.g. `G-ABC123XYZ9`. Empty = no analytics code at all. An invalid value stops the site at startup with a clear message |
| `RequireConsent` | `true` (default) shows a cookie banner and uses **Google Consent Mode v2**: analytics storage is *denied* until the visitor accepts, so only cookieless pings are sent before consent. Set `false` only where no consent is required |

**Where to get the ID:** [analytics.google.com](https://analytics.google.com) →
*Admin* → *Create* → *Property* (or use an existing one) → *Data streams* →
*Add stream* → *Web* → enter `https://sql-backup.kaizar.us` → copy the
**Measurement ID** (`G-…`).

The site sends these events in addition to GA4's automatic page views:
`download_cta` / `download_click` (download buttons), `source_click` (GitHub
button) and `generate_lead` (contact form sent). In GA4 you can mark
`generate_lead` as a key event under *Admin → Events*.

### `GoogleSearchConsole`

Use **one** of the two verification methods Search Console offers:

| Key | Search Console method | What to paste |
|---|---|---|
| `VerificationToken` | *HTML tag* | Only the `content` value of `<meta name="google-site-verification" content="THIS_PART" />` |
| `HtmlVerificationFile` | *HTML file* | Only the file name, e.g. `google0123456789abcdef.html` — the site serves it with the content Google expects |

**Steps:** [search.google.com/search-console](https://search.google.com/search-console)
→ *Add property* → *URL prefix* → `https://sql-backup.kaizar.us` → choose *HTML
tag* (or *HTML file*) → copy the value into `appsettings.json` → deploy → click
*Verify*. Then open *Sitemaps* and submit `sitemap.xml`
(`https://sql-backup.kaizar.us/sitemap.xml`). A *Domain* property (DNS TXT
record) also works and needs no setting here.

### `ContactEmail` — where contact-form messages go

| Key | Example | Purpose |
|---|---|---|
| `ToAddress` | `you@example.com` | Recipient(s); separate several with `;` or `,` |
| `ToName` | `Your Name` | Optional display name |
| `FromAddress` | `website@example.com` | Sender. Must be an address your SMTP account may send as |
| `FromName` | `SQL Server Backup Suite website` | Sender display name |
| `SubjectPrefix` | `[sql-backup.kaizar.us]` | Prepended to the subject, handy for mail rules |
| `Smtp:Host` | `smtp.example.com` | SMTP server |
| `Smtp:Port` | `587` | Usually 587 (STARTTLS) or 465 (implicit TLS) |
| `Smtp:Security` | `StartTls` | `StartTls`, `SslOnConnect` (port 465), `StartTlsWhenAvailable`, `Auto` or `None` (anything else stops the site at startup) |
| `Smtp:Username` / `Smtp:Password` | | SMTP login; leave empty for an unauthenticated relay |
| `Smtp:TimeoutSeconds` | `30` | Connection/send timeout |
| `PickupDirectory` | empty | When set, messages are saved as `.eml` files in this folder instead of being sent (local testing) |

The visitor's address is set as **Reply-To**, so replying to the email answers
them directly. The email contains name, email, company, topic, message, time,
IP address and browser.

Typical SMTP settings:

| Provider | Host | Port | Security | Notes |
|---|---|---|---|---|
| Gmail / Google Workspace | `smtp.gmail.com` | 587 | `StartTls` | Use an [app password](https://myaccount.google.com/apppasswords) (2-step verification required) |
| Microsoft 365 | `smtp.office365.com` | 587 | `StartTls` | The mailbox needs *Authenticated SMTP* enabled; many tenants disable it — a relay service is often easier |
| SendGrid | `smtp.sendgrid.net` | 587 | `StartTls` | Username `apikey`, password = API key; verify the sender address |
| Mailgun / Amazon SES / Brevo | provider's SMTP host | 587 | `StartTls` | Use the SMTP credentials from the provider |

> **Keep the SMTP password out of Git.** The keys are in `appsettings.json`, but
> any of them can be overridden without editing the file: environment variable
> `ContactEmail__Smtp__Password` (double underscore = section separator), Azure
> App Service *Configuration*, IIS environment variables, or `dotnet user-secrets`
> during development. The same works for every other key.

If the contact settings are incomplete, the site logs a warning at startup and
the form shows a friendly error instead of failing.

### `ContactForm` — spam and abuse protection

| Key | Default | Purpose |
|---|---|---|
| `PermitLimit` | `5` | Messages sent per IP address per window. Only messages that are actually sent count — fixing validation errors never locks a visitor out, and a visitor who hits the limit keeps their text |
| `WindowMinutes` | `15` | Rate-limit window |
| `MinimumSecondsToSubmit` | `3` | Faster submissions are treated as bots |
| `MaximumFormAgeHours` | `12` | Older forms ask the visitor to resend |

The form also has a hidden honeypot field, anti-forgery protection and an email
address check that matches what the mail library can deliver to. Bots that trip
the honeypot or the timer see the normal "thank you" page, so they learn
nothing, but no email is sent. When the form is shown again after an error, the
timer no longer applies, so an immediate resend always goes through.

## Deploy

The site is a standard ASP.NET Core 8 app. Publish it and host it anywhere .NET
runs:

```bash
dotnet publish website/SqlBackup.Website -c Release -o out/website
```

* **IIS (Windows Server):** install the
  [ASP.NET Core Hosting Bundle](https://dotnet.microsoft.com/download/dotnet/8.0),
  create a site pointing at the publish folder, bind `sql-backup.kaizar.us` with
  an HTTPS certificate.
* **Azure App Service:** `az webapp up` or deploy the publish folder; put secrets
  in *Configuration → Application settings* (e.g. `ContactEmail__Smtp__Password`).
* **Linux + nginx / Docker / container platforms:** run
  `dotnet SqlBackup.Website.dll` (listens on `ASPNETCORE_URLS`), terminate TLS at
  the proxy, set `Site:BehindReverseProxy` to `true` (with `Site:TrustedProxies`
  unless the app port is private) and point `DataProtection:KeysDirectory` at a
  persistent volume.

The site sends HSTS and redirects to HTTPS in production. Serve it from exactly
one host name (`sql-backup.kaizar.us`); if you also own `www.`, redirect it at
DNS/proxy level.

## SEO

Built in:

* One `<h1>`, a unique `<title>` (≤ 60 characters where possible) and a unique
  meta description (≤ 160 characters) per page — enforced by tests.
* Canonical URLs on every page; uppercase and trailing-slash variants
  301-redirect to the lowercase canonical URL.
* `sitemap.xml` (every indexed page) and `robots.txt` (points to the sitemap,
  hides the thank-you and error pages). A test fails if a new page is added
  without deciding whether it belongs in the sitemap (`Seo/SitePages.cs`).
* Open Graph and Twitter cards with a 1200×630 share image
  (`wwwroot/img/og-image.png`); docs pages use a relevant screenshot.
* JSON-LD structured data: `SoftwareApplication` and `WebSite` (home),
  `BreadcrumbList` (inner pages), `TechArticle` (guides), `FAQPage` (FAQ).
* Descriptive `alt` text on every screenshot, explicit `width`/`height` (no
  layout shift), lazy loading below the fold, a preloaded hero image, WebP at 1x
  and 2x, Brotli/gzip compression and long-lived caching of versioned assets.
* Mobile-friendly layout, light and dark themes, a skip link and keyboard focus
  styles.

### Keyword strategy

Each page targets one search intent, so pages don't compete with each other:

| Page | Primary keywords | Supporting / long-tail |
|---|---|---|
| `/` | free SQL Server backup software, SQL Server backup tool | automatic SQL Server backup, open-source SQL Server backup, SQL Server backup Windows service |
| `/sql-server-express-backup` | SQL Server Express backup, schedule SQL Server Express backups | backup without SQL Server Agent, SQL Express automatic backup, Task Scheduler sqlcmd backup |
| `/features` | SQL Server backup features | RESTORE VERIFYONLY, chain-aware retention, RPO alerts, dedicated backup login |
| `/docs/backup-jobs` | SQL Server backup schedule, full differential transaction log backup | cron backup schedule, SQL Server backup retention policy, point-in-time recovery |
| `/docs/off-site-backups` | SQL Server backup to S3 / Azure Blob / Google Drive / SFTP / network share | S3-compatible backup (MinIO, Backblaze B2, Wasabi), off-site SQL Server backups |
| `/docs/monitoring-and-alerts` | SQL Server backup monitoring, backup failure alerts | email / Slack / Teams backup notification, Windows Event Log backup alert |
| `/docs/troubleshooting` | exact error messages people paste into search: "NT AUTHORITY\SYSTEM is not able to access the database", "BACKUP DATABASE permission denied", "Operating system error 5", "error 1053" | |
| `/docs` | install SQL Server backup tool, getting started | dedicated backup login, first scheduled backup |
| `/download`, `/faq` | download free SQL Server backup tool, SQL Server backup FAQ | how to restore a SQL Server backup, is it free |

After launch, check *Search Console → Performance → Queries* every few weeks and
expand the pages whose queries get impressions but few clicks.

## Screenshots

All images in `wwwroot/img/screens` are captures of the real control panel
(the WPF app) connected to a real SQL Server 2022, running real backups and
off-site copies, taken at 200% scaling. They were captured with the app running
under Wine, so the UI font is a Segoe UI look-alike; recapturing on Windows gives
pixel-exact Windows rendering. The 30-day history behind the History and Reports
pages was generated for the demo; the runs at the top of History are real.

Each image exists as `name.webp` (1x) and `name@2x.webp` (exactly twice the
size); the 1x size is registered in `Infrastructure/Screenshots.cs` and both are
checked by a test. To replace one, capture the window at 200% display scaling,
save it as `name@2x.webp`, save a half-size copy as `name.webp`, and update the
size in `Screenshots.cs` if it changed. Use one in a page with:

```cshtml
<screenshot name="dashboard" caption="Optional caption" />
```
