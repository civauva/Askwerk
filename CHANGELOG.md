# Changelog

All notable changes to Askwerk are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and Askwerk uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- One layout language: every page starts at the same place beside the sidebar, left-aligned; lists up to 1,200 px, forms up to 720 px, only canvases full width (AWK-41).
- Survey pages show a breadcrumb, the survey's name and then the page; the sidebar shows only the survey's places (AWK-41).
- The slider shows where to touch before it is answered, ticks per step and numbers at its ends, and can be cleared (AWK-41).
- The PDF prints the pictures of picture choices (AWK-41).
- Deleted organisations are kept 30 days: installation administrators restore them or remove their data at once by typing the short name; after 30 days the data is removed by itself, the audit log keeps the record (AWK-39).
- Administration → Installation is editable: name, language for new organisations, time zone and email with a test email; values from the configuration (ASKWERK_NAME, ASKWERK_LANGUAGE, ASKWERK_TIME_ZONE, ASKWERK_SMTP_*) show as locked (AWK-39).
- The app in a sidebar: the organisation box, Surveys, Templates and one Administration; inside a survey the sidebar steps into it with "All surveys" to go back (AWK-38).
- Administration → Organisation (name, short name, ownership, delete) and This Askwerk for installation administrators: every organisation (create, suspend, resume, delete), administrators, installation settings (AWK-38).
- Templates page: Askwerk's templates, the samples and the organisation's own, one click from a new survey (AWK-38).
- Developers hub in the top bar: integration guides, the API reference with Try it and a one-hour trial key, SDKs and samples, a Postman collection with this Askwerk's address, the OpenAPI description and the SQL Server example as downloads (AWK-34).
- Six richer demo surveys in every organisation, published with their own look, pictures and sample answers: City museum visit and Dream holiday (graphics-rich), Patient experience (healthcare), Course evaluation (education), Digital banking (financial services), Supplier assessment (manufacturing) (AWK-36).
- Panorama, the 17th look: a mountain landscape behind the whole page, questions on a frosted panel, the title over the picture, tinted text blocks; the PDF prints the landscape as a band on page 1. Sample "Mountain lodge" shows it (AWK-35).
- Look is the survey's look definition: a gallery of 16 looks, and "Make it yours" with colours, type, shape, header and pattern, every component, every page, dark mode, email and PDF, beside the one Preview (AWK-33).
- Eight sample surveys under New survey → A sample: graphics-rich, spartan, colourful, monochrome, PDF-ready, kiosk, full tour and dark, each with its look and sample answers (AWK-33).
- Agent and contributor rulebook in `agents/` (AWK-1).
- Repository scaffolding: README, licences, contributing guide, security policy, Docker Compose skeleton, test script stubs, issue and pull request templates (AWK-2).
- Runnable skeleton: API host with health checks, tamper-evident audit log, OpenTelemetry logs/metrics/traces, browser telemetry intake, App API and (empty) Public API documents, a placeholder page in German and English, a native container health probe, and a real Docker image with a smoke test (AWK-11).
- One data grid for every list (AWK-19): sorting, page numbers, loading more while scrolling for responses, cards on phones; the survey list, responses and participants use it, and participants can be reminded or removed in bulk.
- Exports run as background jobs with progress, recent exports and a ready notice; files are kept 7 days (AWK-25).
- Public API v1, first endpoints: API keys (Organisation → API), list and read surveys, start, poll and download exports; problem details in German and English; a rate limit per key (AWK-25).
- Analysts and viewers open a survey's Results instead of the editor; Preview and the PDF are in the Results bar for everyone (AWK-27).
- One bar and one row of tabs on every screen of a survey: Build, Settings, Share, Results, Participants; the survey's menu renames, duplicates, closes, reopens and archives it; pages are renamed, moved, duplicated and deleted in the designer (AWK-29).
- Approve a survey from Preview: every member may approve the saved draft with a note; editors see the approval next to Publish and in the Publish dialog; approving, withdrawing and publishing are in the audit log (AWK-28).
- Members of an organisation: owners and admins invite colleagues by email or with a link valid 7 days, change their role and remove them; colleagues join with a new or their existing account; anyone but the last owner may leave (AWK-30).
- Survey logic: show questions and pages only under conditions, jump forward or end after a page; rules read as sentences, are listed in Settings, checked before publishing and printed in the PDF (AWK-31).
- Webhooks: response.completed, export.completed, survey.published and survey.closed posted signed to your server, with retries, a deliveries log and replay; managed on the API page or through the Public API (AWK-32).
- OAuth 2.0 client credentials: clients on the API page and /api/v1/oauth/token for 15-minute access tokens that work like keys (AWK-32).
- MCP server at /mcp: AI assistants list and read surveys and export responses with a key or OAuth token, never beyond its scopes (AWK-32).
- Public API: responses changed since a sequence, in long format with labels, and the codebook; an example job copies them into SQL Server (docs/integrations/sql-server-sync) (AWK-14).
- SDKs for the Public API in TypeScript (@askwerk/sdk), C# (Askwerk.Sdk) and Python (askwerk): no dependencies, API key or OAuth client, paging, waiting for exports, webhook signature checks (src/sdks) (AWK-26).
- Organisation → Sign-in: your product hands its signed-in users over with a signed token (RS256 or ES256, PEM keys or JWKS), its groups give roles; a provisioning API (users:manage) creates, changes and deactivates users, who are locked on the Members page (AWK-12).
- Single sign-on with your OpenID Connect provider (Entra ID, ADFS, Google, Okta…): a link per organisation and "Sign in with single sign-on" on the sign-in page; groups give roles (AWK-12).
- Sign-in with the directory password (LDAP, Active Directory) over LDAPS, with a test-connection check on the Sign-in page (AWK-12).
- A survey's places are named for what they hold: Create (Questions, Look) · Collect (Share, Participants) · Analyse (Results); the welcome and thank-you pages and all rules sit in the page list; ⚙ Survey settings beside the name holds the name, languages, collecting, duplicate, template and archive; the name renames on click; the profile menu shows inside a survey; pages move with Alt+↑/↓ and have Duplicate and Delete beside their title (AWK-29, W23).
- Settings moved to the right end of the survey's tabs; Create, Collect and Analyse each sit on a tile with an icon (AWK-29, W25).
- Survey settings → Collecting responses: a closing date in the organisation's time zone, after which the link stops taking responses and the survey closes by itself (the bar and the closed page say when); one response per person, on by default, so a browser that finished cannot answer again, or off for a tablet at a stand with "Answer again" on the end page; both in the audit log (AWK-29, W24).
- The designer and Results inside your own product: /embed/designer and /embed/results with a hand-over token, sessions kept in memory, events to the host; a survey can be limited to some of the organisation's websites; a host can name the respondent (AWK-13).

### Fixed
- The question inspector no longer scrolls sideways; picture options get a 40 px slot before their text (AWK-41).
- Picture choices have room between the picture and the border (AWK-41).
- One question at a time: choosing an answer no longer moves on by itself; the respondent moves on with OK or Enter (AWK-37).
- Question texts and page titles scale with the screen on phones instead of jumping at one width (AWK-35).
- "Other, please specify" in the survey sits inside its option with room around it instead of touching the edges (AWK-20).
