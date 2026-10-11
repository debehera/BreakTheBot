# BreakTheBot

**A free, hands-on lab for the OWASP Top 10 for LLM Applications.** Attack deliberately vulnerable AI apps, capture the flag, learn why it worked, then switch on a defense and replay your attack to see whether the fix holds.

**Live demo:** https://breakthebot.onrender.com
*(It runs on a free host that sleeps when idle, so the first load after a quiet spell can take up to a minute.)*

![Home page](docs/screenshots/home.png)

## What is it?

BreakTheBot is a web lab for security professionals and pentesters who know web and API security but have never attacked an LLM app. Each level is a small, realistic AI application with a built-in weakness. You talk to a real Google Gemini model, find the weakness, capture a per-user flag, read an OWASP-based explanation, and then verify a defense by replaying your own attack.

Everything is simulated and built for education. No real emails, refunds or data are involved.

## Screenshots

| | |
|---|---|
| ![Dashboard](docs/screenshots/dashboard.png) | ![Level 1 capture](docs/screenshots/level1-capture.png) |
| ![Defense replay](docs/screenshots/defense-replay.png) | ![Level 4 tool call](docs/screenshots/level4-tool-call.png) |
| ![Level 5 token meter](docs/screenshots/level5-token-meter.png) | ![Leaderboard](docs/screenshots/leaderboard.png) |

## The levels

| # | OWASP item | Level | What you attack | Defense you can test |
|---|---|---|---|---|
| 1 | LLM01 Prompt Injection | SupportBot | A support bot guarding a secret promo code | Input guard, hardened prompt, output filter |
| 2 | LLM07 System Prompt Leakage | Aria | An IT helpdesk bot with a secret in its prompt | Remove the secret from the prompt, canary filter |
| 3 | LLM02 Sensitive Information Disclosure | HR Helper | An HR assistant that sees the whole employee directory | Data minimization, output redaction |
| 4 | LLM06 Excessive Agency | OpsAssistant | An AI that can call (simulated) refund and email tools | Role allowlist, argument validation, approval gate |
| 5 | LLM10 Unbounded Consumption | SummarizeAPI | A summarizer with no input or output limits | Input cap, output token cap |
| 6 | LLM05 Improper Output Handling | ReviewWidget | An app that renders the AI's HTML directly (shown in a sandbox) | Attack and explain only |
| 7 | LLM09 Misinformation | SecAdvisor | An overconfident advisor that invents a vulnerability | Attack and explain only |

**Coming soon:** LLM03 Supply Chain, LLM04 Data and Model Poisoning, LLM08 Vector and Embedding Weaknesses.

## How it works

1. **Attack:** chat with a vulnerable AI app and try to make it misbehave.
2. **Learn:** capture the flag, then read why the attack worked and how OWASP says to prevent it.
3. **Defend:** switch on the defense for that level.
4. **Verify:** replay your winning attack against the defense and see whether it holds.

Flags are unique to each account (an HMAC of your user id and the level, using a server secret), so they cannot be shared or copied from the source code.

## Scoring

- **Attack:** 100 points per level, minus 10 for each hint used (never below 50).
- **Defense:** 50 points per level when your replayed attack is blocked (levels 1 to 5).
- **Maximum:** 950 points. A top-10 leaderboard breaks ties in favor of whoever scored first.

## Tech stack

- .NET 8, ASP.NET Core Razor Pages and minimal APIs, ASP.NET Core Identity
- Entity Framework Core: SQLite locally, PostgreSQL in production
- Google Gemini (free tier) through a small typed HTTP client
- Plain JavaScript for the chat UI, Bootstrap with a custom dark theme
- xUnit tests (about 327), including integration tests that start the whole app
- Docker, deployed on Render with a Neon Postgres database, both on free plans

More detail: [Architecture](docs/ARCHITECTURE.md), [Schema](docs/SCHEMA.md), [API](docs/API.md), [UI wireframes](docs/UI-WIREFRAMES.md), [Project structure](docs/PROJECT-STRUCTURE.md).

## Run it locally

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) and a free Gemini API key from [Google AI Studio](https://aistudio.google.com/).

```powershell
git clone https://github.com/debehera/BreakTheBot.git
cd BreakTheBot/src/BreakTheBot.Web

# Keep secrets out of Git: store them with user-secrets.
dotnet user-secrets init
dotnet user-secrets set "Gemini:ApiKey" "YOUR_GEMINI_KEY"
dotnet user-secrets set "Flags:Secret" "a-long-random-string-of-at-least-32-characters"

cd ../..
dotnet run --project src/BreakTheBot.Web
```

Open the address printed in the terminal (for example http://localhost:5298). The SQLite database file is created automatically on first run.

Free-tier model names and limits change. If the AI calls fail with a "not found" error, set a model your key can use in `src/BreakTheBot.Web/appsettings.json` (`Gemini:Model`).

### Run the tests

```powershell
dotnet test
```

The tests use a fake AI and an in-memory database, so they need no API key, no network, and use no quota.

## Configuration

| Setting | Purpose | Default |
|---|---|---|
| `Gemini:ApiKey` | Your Gemini API key (secret) | none |
| `Gemini:Model` | Model name | `gemini-3.5-flash-lite` |
| `Flags:Secret` | Secret used to generate per-user flags (secret) | none |
| `Database:Provider` | `Sqlite` or `Postgres` | `Sqlite` |
| `ConnectionStrings:Default` | Database connection string | local SQLite file |
| `Limits:PerMinutePerUser` | Chat messages per user per minute | 10 |
| `Limits:PerDayPerUser` | Chat messages per user per day | 40 |
| `Limits:PerDayGlobal` | Chat messages for everyone per day (protects the free AI quota) | 400 |
| `Limits:AuthAttemptsPer10Min` | Sign-in and sign-up attempts per IP per 10 minutes | 10 |

In production, set these as environment variables with a double underscore, for example `Gemini__ApiKey` and `ConnectionStrings__Default`.

## Deploying

The repository contains a `Dockerfile`. A typical free setup:

1. Create a free Postgres database (for example on Neon) and build a connection string such as `Host=...;Database=...;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true`.
2. Create a Web Service on Render from this repository, choose the Docker runtime, and set the environment variables from the table above, including `Database__Provider=Postgres`.
3. Deploy, then check `/healthz` (it returns `ok`).

Free-tier notes: the web service sleeps after 15 minutes without traffic and takes about a minute to wake, and free tiers can change. In production the login keys are stored in the database, so users stay logged in across restarts.

## Security of the app itself

A lab about security should be secure. Highlights: per-user flags that never appear in pages or scripts, server-side win detection, history loaded from the database (never from the browser), anti-forgery tokens on every API call, per-user rate limits and daily caps, a strict Content Security Policy, HttpOnly and SameSite cookies, a sign-in throttle, and AI-generated HTML shown only inside a sandboxed frame. Automated tests cover many of these rules.

## Known limitations

- Display names on the leaderboard are the part of the email before the @ sign, so two players can share a name.
- No password reset, email confirmation, or admin tools.
- The database schema is created with `EnsureCreated` (no migrations), so schema changes require a reset.
- The free AI allowance is shared by all players and can run out for the day.
- Defenses are intentionally partial and educational, not production-grade.

## Roadmap

- Playable levels for LLM03, LLM04 and LLM08
- Harder defense tiers and chained attacks
- Custom display names, password reset, and email confirmation
- Database migrations
- Bring-your-own API key and local models

## Ethical use

Use what you learn here only on systems you own or have explicit written permission to test. Please do not enter personal or sensitive information into the chats: the AI runs on a free Gemini tier, and prompts sent on a free tier may be reviewed or used by Google to improve its products.

## License

MIT. See [LICENSE](LICENSE).
