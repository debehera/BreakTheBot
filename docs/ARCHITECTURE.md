# BreakTheBot — System Architecture

> Version 1.0 · Status: design baseline for the 10-day build · Stack: .NET 8, ASP.NET Core Razor Pages, EF Core, Google Gemini API

## 1. Overview and design principles

BreakTheBot is a server-rendered web application. The browser talks only to the ASP.NET Core app. The app owns all game logic, calls Gemini on the learner's behalf, and stores progress in a relational database.

| Principle | What it means in practice |
|---|---|
| **Server is the authority** | Flags, win detection, scoring, defenses and rate limits all run on the server. The browser never sees a flag until it is earned |
| **One small app, not microservices** | One web project, one database. Fast to build in ~18 hours and trivial to deploy for free |
| **AI behind an interface** | Levels depend on `ILlmClient`, never on Gemini directly. Tests use a fake client; the provider can change later |
| **Levels are plug-ins** | Each level is one class implementing `ILevel`, registered in one registry |
| **Defenses are mostly code, not prompts** | Replay results must be reliable, so server-side filters do the blocking, not the model's mood |
| **Free-tier aware** | Every AI call passes through limits that protect the shared Gemini quota |

## 2. System context

```mermaid
flowchart LR
    L(["Learner<br/>(browser)"])
    subgraph BTB["BreakTheBot (single deployable)"]
        APP["ASP.NET Core web app<br/>Razor Pages + JSON endpoints"]
    end
    G["Google Gemini API<br/>gemini-3.5-flash-lite"]
    DB[("Database<br/>SQLite local / PostgreSQL prod")]
    GH["GitHub<br/>source + releases"]
    R["Render (free)<br/>Docker host"]
    N["Neon (free)<br/>managed Postgres"]

    L -- "HTTPS pages + fetch calls" --> APP
    APP -- "generateContent (REST)" --> G
    APP -- "EF Core" --> DB
    GH -- "deploys on push" --> R
    R -. "runs" .-> APP
    N -. "is the prod" .-> DB
```

## 3. Component diagram

```mermaid
flowchart TB
    subgraph Browser
        P["Razor-rendered pages<br/>Home · Dashboard · Play · Leaderboard · About"]
        JS["chat.js<br/>fetch + DOM updates (textContent only)"]
    end

    subgraph Web["BreakTheBot.Web (ASP.NET Core)"]
        direction TB
        MW["Middleware pipeline<br/>ForwardedHeaders · ExceptionHandler · StaticFiles<br/>Authentication · Authorization · RateLimiter · Antiforgery"]
        PG["Razor Pages<br/>(UI, server-rendered)"]
        EP["Minimal API endpoints<br/>/api/levels/*"]

        subgraph Services
            LE["LevelEngine<br/>(orchestrator)"]
            UL["UsageLimiter"]
            FS["FlagService (HMAC)"]
            SS["ScoringService"]
            LB["LeaderboardService"]
            TE["ToolExecutor<br/>(simulated tools, Level 4)"]
        end

        subgraph Levels
            LR["LevelRegistry"]
            L1["Level1 SupportBot"]
            L2["Level2 Aria"]
            L3["Level3 HR Helper"]
            L4["Level4 OpsAssistant"]
            L5["Level5 SummarizeAPI"]
            L6["Level6 ReviewWidget"]
            L7["Level7 SecAdvisor"]
        end

        LLM["ILlmClient"]
        GC["GeminiClient (typed HttpClient)"]
        DBX["AppDbContext (EF Core + Identity)"]
    end

    EXT["Gemini REST API"]
    DB[("Database")]

    P --> MW
    JS --> MW
    MW --> PG
    MW --> EP
    PG --> LB
    PG --> SS
    EP --> LE
    LE --> UL
    LE --> LR
    LE --> FS
    LE --> SS
    LR --> L1 & L2 & L3 & L4 & L5 & L6 & L7
    L4 --> TE
    L1 & L2 & L3 & L4 & L5 & L6 & L7 --> LLM
    LLM --> GC
    GC --> EXT
    LE --> DBX
    UL --> DBX
    SS --> DBX
    LB --> DBX
    DBX --> DB
```

Dependency rule: **Levels never touch the database or HTTP.** They receive a `LevelContext` and return a `LevelReply`. Only `LevelEngine` and the services read or write data.

## 4. Data flow

```mermaid
flowchart LR
    A["Learner types a message"] --> B["chat.js POST /api/levels/{id}/chat"]
    B --> C["Auth + anti-forgery + rate limit"]
    C --> D["LevelEngine: load progress and last 6 turns"]
    D --> E["UsageLimiter: per-user and global caps, length check"]
    E -->|blocked| X["Friendly error JSON"]
    E -->|ok| F["Level: input guard and prompt build"]
    F --> G["ILlmClient to Gemini"]
    G --> H["Level: output filter and win detection"]
    H --> I["Save ChatLog, update LevelProgress"]
    I --> J["JSON reply: text, systemNote, blocked, exploitDetected, usage"]
    J --> K["chat.js renders bubble"]
```

## 5. Request lifecycle (chat call)

```mermaid
sequenceDiagram
    autonumber
    participant B as Browser (chat.js)
    participant M as Middleware
    participant E as Endpoint
    participant LE as LevelEngine
    participant UL as UsageLimiter
    participant LV as Level (ILevel)
    participant GC as GeminiClient
    participant G as Gemini API
    participant DB as Database

    B->>M: POST /api/levels/3/chat {message, defenseOn} + auth cookie + RequestVerificationToken
    M->>M: authenticate, antiforgery, per-user rate limit (10/min)
    alt not logged in
        M-->>B: 401
    else over rate limit
        M-->>B: 429 + Retry-After
    end
    M->>E: forward
    E->>LE: ChatAsync(userId, levelId, message, defenseOn)
    LE->>DB: load LevelProgress, last 6 non-reset ChatLog rows
    LE->>UL: Check(userId, message length)
    UL->>DB: count today's calls (user, global)
    UL-->>LE: ok or reason
    LE->>LV: HandleAsync(ctx{flag, defenseOn, history, llm}, message)
    LV->>LV: input guard (defense only), build system prompt
    LV->>GC: GenerateAsync(LlmRequest)
    GC->>G: POST models/gemini-3.5-flash-lite:generateContent
    G-->>GC: text + usageMetadata (or 429 / safety block)
    GC-->>LV: LlmResult
    LV->>LV: output filter (defense only), win detection
    LV-->>LE: LevelReply
    LE->>DB: insert ChatLog, update Attempts and Status
    LE-->>E: DTO
    E-->>B: 200 JSON
```

## 6. The learning loop (state model)

```mermaid
stateDiagram-v2
    [*] --> NotStarted
    NotStarted --> InProgress: first chat message
    InProgress --> Captured: correct flag submitted (+attack points)
    Captured --> Defended: replay blocked with defense ON (+50)
    Captured --> Captured: replay still vulnerable
    Defended --> [*]
```

Levels 6 and 7 have no defense toggle, so they end at `Captured`.

### Replay sequence

```mermaid
sequenceDiagram
    participant U as Learner
    participant API as /replay endpoint
    participant LV as Level
    participant S as ScoringService
    U->>API: Replay my attack (defense ON)
    API->>API: require status Captured and stored WinningPrompt
    API->>LV: HandleAsync(defenseOn = true, empty history, WinningPrompt)
    alt no exploit detected
        API->>S: AwardDefenseAsync (idempotent)
        API-->>U: Blocked + 50 points
    else exploit still works
        API-->>U: Still vulnerable + guidance
    end
```

## 7. AI interaction design

### 7.1 Prompt assembly

```mermaid
flowchart TB
    S["System prompt (built per request by the Level)<br/>persona + scenario rules + per-user flag (vulnerable mode)"]
    H["History: last 6 turns from ChatLog<br/>(server-side only, cannot be forged)"]
    U["User message<br/>(wrapped in delimiters when defense ON)"]
    S --> R["LlmRequest"]
    H --> R
    U --> R
    R --> C["GeminiClient"]
    C --> G["generateContent<br/>systemInstruction + contents + generationConfig"]
```

### 7.2 Rules for the AI layer

| Topic | Decision |
|---|---|
| Model | `gemini-3.5-flash-lite` (config key `Gemini:Model`). Verified on Day 2: works on the free tier, ~8 tokens for a one-word reply. `gemini-3.5-flash` is the fallback but allows only ~20 requests/day |
| Free-tier limits (observed Day 2) | 15 requests/min, 500 requests/day for the model. Shared by all users |
| App-level caps | 10 calls/min/user, 40/day/user, 400/day global, 4000 chars max input, 1024 max output tokens |
| History window | Last 6 turns, loaded from the database, never from the client |
| Win detection | Server-side, never trusts the client. Flag in output (levels 1–3), tool execution (4), token threshold (5), regex (6), CVE mention without hedges (7) |
| Determinism | Filters and validators decide outcomes. Model text is allowed to vary |
| Errors | 429 → "AI is busy, try again"; safety block → explain and suggest rewording; timeout (30 s) / 5xx → friendly retry message. Never expose raw provider errors |
| Token accounting | `usageMetadata` is stored on every `ChatLog` row and returned to the UI (Level 5 meter) |
| Privacy | Free-tier prompts may be used by Google to improve its products; the About page tells learners not to enter personal data |

### 7.3 Per-level processing pipeline

```mermaid
flowchart LR
    I["User input"] --> G1{"Defense ON?"}
    G1 -- yes --> IG["Input guard / delimiters / length cap"]
    G1 -- no --> PB
    IG -->|rejected| BL["Blocked reply (no AI call)"]
    IG --> PB["Build system prompt<br/>(vulnerable or hardened variant)"]
    PB --> AI["ILlmClient"]
    AI --> G2{"Defense ON?"}
    G2 -- yes --> OF["Output filter / redaction / tool policy"]
    G2 -- no --> WD
    OF --> WD["Win detection"]
    WD --> OUT["LevelReply"]
```

## 8. External services

| Service | Purpose | Cost | Risk and mitigation |
|---|---|---|---|
| Google AI Studio / Gemini API | The vulnerable AI targets | Free tier | Quota exhaustion → caps and friendly errors; model retirements (2.5 models already 404) → model name is config-driven |
| GitHub | Source, releases | Free | None significant |
| Render (free web service) | Hosts the Docker image | Free | Sleeps after inactivity (cold start) → documented; verify availability on Day 9 |
| Neon (free Postgres) | Production database | Free | Free limits may change → verify Day 9; fallback host noted in blueprint |

## 9. Database (summary)

SQLite locally and PostgreSQL in production, switched by `Database:Provider`. Schema is created with `EnsureCreated()` (no migrations in v1.0). Full detail in `SCHEMA.md`.

```mermaid
erDiagram
    AspNetUsers ||--o{ LevelProgress : has
    AspNetUsers ||--o{ ChatLog : writes
```

## 10. Deployment architecture

```mermaid
flowchart LR
    DEV["Developer machine<br/>VS Code + dotnet run + SQLite"] -- "git push" --> GH["GitHub repo"]
    GH -- "auto build" --> R["Render free web service<br/>Dockerfile build"]
    R --> C["Container: ASP.NET Core on :8080"]
    C -- "Npgsql (SSL)" --> N[("Neon Postgres")]
    C -- "HTTPS" --> G["Gemini API"]
    U(["Internet users"]) -- "HTTPS (TLS terminated by host)" --> R
```

- Secrets (`Gemini__ApiKey`, `Flags__Secret`, connection string) are environment variables on the host and **user-secrets** locally. Nothing secret is committed.
- `UseForwardedHeaders` makes the app trust the host's `X-Forwarded-Proto/For` headers so cookies and redirects work behind the proxy.

## 11. Security architecture

```mermaid
flowchart LR
    subgraph Untrusted
        BR["Browser / learner input"]
        LO["LLM output"]
    end
    subgraph Trusted["Trusted server code"]
        SRV["Level logic, scoring, flags, limits"]
    end
    BR -- "validate, authorize, antiforgery" --> SRV
    LO -- "treat as untrusted data" --> SRV
```

| Threat | Control |
|---|---|
| Flag theft or sharing | Per-user HMAC flags (`Flags:Secret`), generated server-side, never in source or HTML |
| Cheating via forged history | History loaded from the database, never from the request |
| IDOR (reading others' progress) | User id comes from the authenticated principal, never from the request body |
| CSRF | Anti-forgery token header on every POST |
| XSS | Chat rendered with `textContent`; Level 6 AI HTML only inside `<iframe sandbox>` without `allow-same-origin` |
| Quota abuse | Layered limits (per-minute, per-user daily, global daily, input/output caps) |
| Secret leakage | user-secrets and env vars; repo scanned before release |
| Simulated tools | Level 4 tools operate on in-memory fake data only |

## 12. Key decisions

| # | Decision | Why | Trade-off |
|---|---|---|---|
| 1 | Razor Pages + minimal APIs + vanilla JS | Smallest moving parts for a solo, 2 h/day build | Less "app-like" than a SPA |
| 2 | Single web project | Fast, simple Docker build | Less strict layering (enforced by folder rules instead) |
| 3 | Typed `HttpClient` to Gemini, no SDK | Fewer dependencies, full control of errors | Manual JSON mapping |
| 4 | `EnsureCreated`, no migrations | Saves hours | Schema changes need a reset; documented limitation |
| 5 | SQLite local + Postgres prod | Zero local setup, free prod DB | Two providers to test |
| 6 | Server-side defenses and checks | Reliable replay results | Defenses are intentionally partial and educational |
| 7 | Levels as classes in a registry | Adding a level is one file | Prompts live in code, so changes need a rebuild |
| 8 | Config-driven model name | Models get retired (2.5 models already 404 on this key) | Needs periodic verification |

## 13. Capacity budget

Free quota is 500 Gemini requests/day. Reserve ~100 for development and testing, leaving a global cap of 400. With a 40/day/user cap, at least 10 active learners per day can finish multiple levels. Level 1 typically takes 5–10 chat calls; a full run of all 7 levels is roughly 60–100 calls, so heavy users will hit their cap, and that is acceptable for v1.0. Limits live in `appsettings.json` and can be tuned without code changes.

## 14. Observability

- Built-in ASP.NET Core logging at `Information`, `Warning` for framework noise.
- Log: Gemini failures (status only, never the key), rate-limit hits, flag submissions (success/fail, no flag text), startup configuration problems.
- Never log: API keys, flags, passwords, full prompts in production.
- The host's log viewer (Render dashboard) is the monitoring tool for v1.0.
