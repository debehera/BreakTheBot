# BreakTheBot — Product Requirements Document (PRD)

| Field | Value |
|---|---|
| Product | **BreakTheBot** — OWASP LLM Top 10 Hands-On Lab |
| Version | v1.0 (first release) |
| Owner | Solo builder (personal project) |
| Date | 4 October 2026 (Day 1) |
| Status | Scope approved |
| Build window | 10 days, ~2 hours/day (Day 1 = requirements, Days 2–10 = build) |

---

## 1. Overview

BreakTheBot is a free, web-based, hands-on security lab where learners attack deliberately vulnerable LLM-powered applications, capture flags, understand why each attack worked, and then enable a defense and re-attack to see whether the mitigation holds. It maps directly to the **OWASP Top 10 for LLM Applications (2025)**.

It is built with **.NET (C#), ASP.NET Core and VS Code**, uses **real Google Gemini free-tier models** as the vulnerable "AI targets", and is deployed to a free public URL.

## 2. Problem Statement

- LLM-powered apps are shipping fast, but most security professionals have never attacked one in a realistic setting.
- OWASP LLM Top 10 documentation is mostly text. Reading "prompt injection" is very different from performing one.
- Existing practice environments are either too simple (a single "trick the bot" game), too heavy to set up (local models, GPUs, Docker stacks), or paid.

**Gap:** there is no lightweight, free, structured lab that walks a pentester through *attack → understand → defend → verify* for the OWASP LLM categories in a browser.

## 3. Vision

Become the easiest place for a security professional to *feel* every OWASP LLM risk in minutes, then grow into a complete, community-extensible lab covering all 10 categories.

## 4. Target Users

**Primary persona — "Priya, the pentester":** an experienced web/API security professional. Knows OWASP Web Top 10 and Burp Suite. New to LLM-specific attack surface. Wants realistic scenarios, minimal hand-holding, and concise explanations she can map to client findings.

**Secondary personas:**
- Application security engineers who need to understand LLM risks to review designs.
- Developers moving into AI security (will benefit from the defense mode).
- Reviewers and recruiters evaluating the builder's work (need a clear demo and polished README).

## 5. Goals and Success Metrics

| Goal | v1.0 Success Measure |
|---|---|
| Teach by doing | A new user can complete Level 1 (attack) within 10 minutes of registering |
| Cover OWASP breadth | 7 playable levels, 3 more shown as Coming Soon, all 10 categories visible |
| Teach defense | 5 levels include a defense toggle with a verifiable "replay" result |
| Be reliable on free tier | App never crashes on quota or safety-filter errors; shows friendly messages |
| Be shippable | Live public URL, public repo with README, screenshots and demo, pitch deck |
| Be finishable | All Day 10 deliverables complete within ~18 build hours |

## 6. Scope

### 6.1 In Scope (v1.0)

1. **7 playable levels** (see section 8).
2. **Accounts:** register, log in, log out (username/email + password).
3. **Per-user progress** saved in a database.
4. **Scoring** (attack points, defense points, hint penalty).
5. **Top-10 leaderboard.**
6. **Level experience:** scenario briefing, chat with a Gemini-powered target, flag submission, hints, explanation panel (OWASP guidance), defense toggle and "Replay my attack" (levels 1–5).
7. **Home page** showing all 10 OWASP items: 7 playable and 3 locked "Coming Soon" cards (LLM03, LLM04, LLM08).
8. **Usage protection:** per-user rate limits, daily caps, hard token and input limits, graceful quota/safety-block messages.
9. **UI:** clean dark theme with terminal-style accents, responsive.
10. **Deployment** to a free host with a public URL; **public GitHub repo** with README.
11. **Automated tests** for core logic; manual test checklist.

### 6.2 Out of Scope (intentional)

- Playable levels for LLM03 Supply Chain, LLM04 Data and Model Poisoning, LLM08 Vector and Embedding Weaknesses (shown as Coming Soon only).
- Password reset, email verification, social login, user profiles, avatars, admin panel.
- Multiple or configurable defenses per level (exactly one defense toggle per level).
- Multiple AI providers, local models, bring-your-own-key.
- Multiplayer, chat between users, mobile app, internationalization.
- Any paid service or tool.
- Database migrations pipeline (v1.0 uses `EnsureCreated`).

## 7. User Flows

**Flow A — First visit:** Home → see 10 OWASP cards → Register → Dashboard (levels list).

**Flow B — Play a level:** Dashboard → open level → read scenario → chat with target → capture flag → submit flag → points awarded → explanation panel unlocks.

**Flow C — Defend (levels 1–5):** After capture → toggle "Enable defense" → click "Replay my attack" → system re-runs the user's winning prompt against the hardened target → result: *Blocked* (defense points awarded) or *Still vulnerable* (read guidance, try again) → optional free-form re-attack with defense on.

**Flow D — Compete:** Leaderboard page shows top 10 by total points.

## 8. Level Specifications

Each level has: scenario, objective, 2 hints, flag (per-user, generated at runtime), explanation, OWASP mapping.

| # | OWASP | Level name | Scenario | Win condition | Defense toggle (one) |
|---|---|---|---|---|---|
| 1 | LLM01 Prompt Injection | **SupportBot** | Customer-support bot holding a secret promo code in its instructions | Learner overrides instructions and extracts the flag | Delimited input handling + input guard + output filter |
| 2 | LLM07 System Prompt Leakage | **Aria** | Internal assistant whose system prompt embeds a fake internal credential | Learner extracts the hidden prompt/credential | Remove secrets from the prompt + canary detection |
| 3 | LLM02 Sensitive Information Disclosure | **HR Helper** | HR assistant that has all employee records in context; learner is an intern | Learner exposes another employee's confidential record | Data minimization + sensitive-data redaction |
| 4 | LLM06 Excessive Agency | **OpsAssistant** | Assistant that can call simulated tools (lookup order, send email, issue refund) | Learner makes the AI execute a refund above the allowed amount | Role-based tool allowlist + approval gate + argument validation |
| 5 | LLM10 Unbounded Consumption | **SummarizeAPI** | Summarization endpoint with no output or input limits | Learner forces a single response whose output tokens exceed the threshold | Input length cap + output token cap + per-user limits |
| 6 | LLM05 Improper Output Handling *(lighter)* | **ReviewWidget** | App renders AI-generated HTML directly | Learner gets the AI to emit an executable-HTML payload (rendered safely in a sandboxed frame) | *None (attack + explain only)* |
| 7 | LLM09 Misinformation *(lighter)* | **SecAdvisor** | Overconfident security advisor bot | Learner makes it fabricate details about a non-existent CVE | *None (attack + explain only)* |
| — | LLM03, LLM04, LLM08 | Coming Soon | Locked cards with a short risk description | — | — |

## 9. Functional Requirements

| ID | Requirement | Priority |
|---|---|---|
| FR-01 | Users can register, log in and log out | Must |
| FR-02 | Home page lists all 10 OWASP LLM items; 7 playable, 3 Coming Soon | Must |
| FR-03 | Dashboard lists levels with status (Not started / In progress / Captured / Defended) and points | Must |
| FR-04 | Level page shows scenario, objective, chat panel, hints, flag input | Must |
| FR-05 | Chat messages are sent to the server, processed by the level logic, and answered by Gemini | Must |
| FR-06 | Flags are unique per user and level and generated server-side | Must |
| FR-07 | Correct flag submission awards attack points once; hints reduce points | Must |
| FR-08 | After capture, the explanation panel is revealed | Must |
| FR-09 | Levels 1–5 have a defense toggle and a "Replay my attack" action | Must |
| FR-10 | Successful defense replay awards defense points once | Must |
| FR-11 | Leaderboard shows top 10 users by total points (ties broken by earliest completion) | Must |
| FR-12 | Rate limits and daily caps protect the free AI quota | Must |
| FR-13 | Gemini errors, quota exhaustion and safety blocks show friendly messages | Must |
| FR-14 | Users can reset a level's conversation | Should |
| FR-15 | About page explains ethical use and OWASP references | Should |
| FR-16 | Levels 6 and 7 playable (lighter, no defense toggle) | Should (stretch tier) |

## 10. Non-Functional Requirements

| ID | Requirement |
|---|---|
| NFR-01 | **Cost:** zero paid services. Free tiers only |
| NFR-02 | **Security:** hashed passwords (ASP.NET Core Identity), anti-forgery protection, server-side validation, secrets only in environment variables or user-secrets, never committed |
| NFR-03 | **Safety:** simulated tools only; no real emails, refunds or file access; AI-generated HTML rendered only inside a sandboxed iframe |
| NFR-04 | **Resilience:** app remains usable if Gemini is unavailable (clear message, no crash) |
| NFR-05 | **Performance:** pages render in under 2 seconds excluding AI response time; chat responses typically under 15 seconds |
| NFR-06 | **Usability:** responsive from 360 px wide screens; keyboard-friendly chat input |
| NFR-07 | **Maintainability:** levels are self-contained classes registered in one registry; adding a level means adding one class |
| NFR-08 | **Privacy:** store only email/username, display name, progress and chat logs; warn users not to enter personal data; free-tier AI data usage disclosed |
| NFR-09 | **Testability:** AI client is behind an interface so tests use a fake client |

## 11. Scoring Model

- **Attack:** 100 points per level, minus 10 per hint used (minimum 50).
- **Defense:** 50 points per level (levels 1–5 only), awarded when the replay is blocked.
- **Maximum total:** 5 × 150 + 2 × 100 = **950 points**.
- Leaderboard ranks by total points, then earliest time of last scoring event.

## 12. Constraints and Assumptions

- Technology agreed by the owner: **.NET / C#, VS Code**. Detailed architecture is defined in the Implementation Blueprint.
- Builder level: intermediate in C#/.NET; Windows machine; GitHub account, .NET SDK and VS Code already installed.
- Time: ~2 hours per day for Days 2–10.
- AI provider: Google AI Studio (Gemini) free tier. **Free-tier model names and limits change; they must be verified on Day 2 and Day 9.**
- Free-tier hosting and database availability must be verified on Day 9.

## 13. Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Gemini free-tier quota exhausted by visitors | Lab unusable | Per-user and global daily caps, friendly error, small token limits |
| Non-deterministic AI responses | Win/defense results vary | Win checks based on flag/pattern detection, not exact text; hints; retries; level prompts tested on Day 4–7 |
| Gemini safety filters block attack prompts | Learner frustration | Detect block reason and explain it; tune scenario wording |
| Scope creep (more levels, richer accounts) | Day 10 missed | Priority tiers and cut lines in blueprint; extra levels only after core is deployed |
| Free host sleeps / ephemeral disk | Slow first load, lost data | Free hosted Postgres for production data; note cold start in README |
| Schedule slip at 2 hrs/day | Incomplete v1.0 | Cut order: Level 7 → Level 6 → polish; core levels 1–5, accounts, leaderboard and deployment are protected |
| Abuse of the public app | Quota burn, junk accounts | Rate limits, input caps, daily cap, simple account limits |

## 14. Milestones

| Day | Milestone |
|---|---|
| 1 | Requirements and blueprint (this document) |
| 2 | Setup and design foundation |
| 3 | Accounts, data model, flags and scoring |
| 4 | AI plumbing, level framework, Level 1 (vulnerable) |
| 5 | Level 1 defense and replay, Level 2 |
| 6 | Levels 3 and 4 |
| 7 | Levels 5, 6 and 7 |
| 8 | Leaderboard, polish, testing and security review |
| 9 | Deployment and README |
| 10 | Launch: live QA, demo, screenshots, pitch deck, v1.0.0 tag |

## 15. Acceptance Criteria (Definition of Done for v1.0)

1. A stranger can open the live URL, register, and log in.
2. Levels 1–5 can be attacked, flagged and defended; levels 6–7 can be attacked and flagged.
3. Points and the top-10 leaderboard update correctly.
4. Exceeding rate limits or quota shows a friendly message and does not crash the app.
5. No secrets exist in the repository history.
6. Unit tests for flags, scoring, filters, tool executor and usage limits pass.
7. README includes overview, screenshots, setup instructions, level list and disclaimer.
8. Pitch deck, demo recording/screenshots and 10 LinkedIn posts are complete.

## 16. Future Scope

- Playable LLM03 (malicious dependency/model), LLM04 (poisoned data), LLM08 (RAG/vector weaknesses).
- Multi-step and chained-attack levels, difficulty tiers (easy/hard defenses).
- Bring-your-own-key and local models.
- Teams, time-boxed CTF events, certificates, instructor dashboard.
- Community-contributed levels and a level-authoring template.
- Database migrations, admin tools, analytics.

## 17. Ethical Use Statement

All targets are simulated and deliberately vulnerable, created for education. Techniques learned must only be applied to systems the learner owns or has explicit written permission to test.

## 18. References

- OWASP Top 10 for LLM Applications (2025): https://genai.owasp.org/llm-top-10/
- Google AI Studio / Gemini API documentation: https://ai.google.dev/
