# Decisions

Choices made where `docs/phase-0-spec.md` was silent or ambiguous. Newest at the bottom. One entry per decision.

Format:

```
## YYYY-MM-DD · T<n> · <short title>
**Context:** what the spec left open.
**Decision:** what was chosen.
**Why:** the reason, in a sentence or two.
```

## 2026-09-28 · Pre-T0 · Blazor interactive server over WebAssembly
**Context:** Phase 0 is single-user and runs on localhost.
**Decision:** Blazor Web App with global `InteractiveServer` render mode, prerendering off. Components call services in-process; no REST API.
**Why:** Removes the API and streaming-protocol layers entirely. Revisit (Auto mode or WebAssembly plus an API) when remote or phone access arrives.

## 2026-09-28 · Pre-T0 · Key-value settings table
**Context:** Settings will grow over time.
**Decision:** `AppSettings(Id, Key, Value, UpdatedAt)`, accessed only through `SettingsService` and `SettingDefinitions`.
**Why:** New settings need no migration. Trade-off: `Chat.DefaultModelId` has no foreign key, so the service treats a missing or unavailable model as unset.

## 2026-09-28 · T0 · Repo root and docs location
**Context:** T0 (manual) includes creating the git repo; it did not exist when T1 started, and the spec and decisions log sat at the folder root.
**Decision:** `git init` in this folder (it plays the role of `localchat/` in the layout), with the spec moved to `docs/phase-0-spec.md` and this log to `docs/decisions.md`, committed as `T0`.
**Why:** T1 needs a repo to commit to, and `CLAUDE.md` already points at the `docs/` paths.

## 2026-09-28 · T1 · Restore from nuget.org only
**Context:** The machine's global NuGet config also lists private feeds.
**Decision:** A repo-level `nuget.config` clears inherited sources and adds only nuget.org.
**Why:** A clean clone restores the same way on any machine, and package queries don't go to unrelated feeds.

## 2026-09-28 · T1 · HTTP only, one place for the URL
**Context:** The template offers HTTPS, and `launchSettings.json` can override `Urls`.
**Decision:** Scaffolded with `--no-https` (no HTTPS redirection or HSTS). `Urls` in `appsettings.json` is the only binding setting; the launch profile sets no `applicationUrl`, so `dotnet run` uses it too.
**Why:** The spec binds `http://localhost:5080` only, and a second copy of the URL would drift.

## 2026-09-28 · T1 · Host filtering limited to loopback names
**Context:** The template allows any `Host` header (`AllowedHosts: *`).
**Decision:** `AllowedHosts` is `localhost;127.0.0.1;[::1]`; other hosts get 400.
**Why:** Binding to loopback alone does not stop DNS-rebinding pages in the browser from reaching an app with no authentication. Revisit when remote access arrives.

## 2026-09-28 · T1 · Fluent UI Blazor v5
**Context:** The spec names Fluent UI Blazor without a version; 5.0.0 is the latest stable.
**Decision:** Use `Microsoft.FluentUI.AspNetCore.Components` 5.0.0: `AddFluentUIComponents()`, the bundled stylesheet in `App.razor`, `<FluentProviders />` at the end of `MainLayout`. Its JS lazy-loads SortableJS and IMask from unpkg.com, but only for sortable lists and masked inputs, so those components are off limits (or their scripts must be vendored first).
**Why:** Latest stable, per the spec; the unpkg loads would break the no-CDN rule.

## 2026-09-28 · T1 · Test stack
**Context:** xUnit v3 4.x uses Microsoft.Testing.Platform (MTP), which no longer runs in `dotnet test`'s VSTest mode on the .NET 10 SDK.
**Decision:** xUnit v3 4.0.1, bUnit 2, and `Microsoft.AspNetCore.Mvc.Testing` for endpoint tests. `global.json` opts `dotnet test` into MTP; `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio` stay for IDE test explorers, as xUnit recommends. The test project uses the Web SDK (it includes the Razor SDK that bUnit needs).
**Why:** Latest stable versions, and the documented setup for the .NET 10 SDK.

## 2026-09-28 · T1 · Health stub and kept template plumbing
**Context:** T1 asks for a stub `/api/health` and an empty layout; T3 and T9 fill them in.
**Decision:** The stub returns `{"status":"ok"}`. The template's `Error` and `NotFound` pages, `ReconnectModal` and the `#blazor-error-ui` bar stay (Bootstrap classes removed); T9 restyles them.
**Why:** They are framework plumbing, not demo pages, and removing them would leave errors and reconnects with no UI.

## 2026-09-28 · T2 · Table names
**Context:** The spec names the tables Project, Conversation, Message, Model and AppSettings but doesn't say whether that is the SQL name.
**Decision:** EF Core's convention: tables are named after the `DbSet` properties (`Projects`, `Conversations`, `Messages`, `Models`, `AppSettings`). Entities keep the spec's singular names; the settings row class is `AppSetting`.
**Why:** No mapping code, and the settings table gets exactly the spec's name.

## 2026-09-28 · T2 · Roles and the General id
**Context:** `System` is a reserved message role; the General id is to be "a constant".
**Decision:** `MessageRole` has only `User` and `Assistant`. `ProjectIds.General` is `static readonly`.
**Why:** Roles are stored as strings, so adding `System` later needs no migration. C# has no `const Guid`.

## 2026-09-28 · T2 · Startup order and seeding location
**Context:** The spec lists the seeding steps but not where they run, and T2 needs the `Ollama.BaseUrl` row before `SettingsService` exists in full (T3).
**Decision:** `Program.cs` awaits `Seed.RunAsync` after `Build()` and before the app starts listening. `SettingsService.SeedAsync` inserts the `Ollama.BaseUrl` row, so only `SettingsService` touches that table even during seeding. The stale-`Streaming` fix runs right after the seed data, ahead of the model sync (T4), so it never depends on Ollama.
**Why:** No request can arrive before the database is ready, and the table rule holds from the first commit.

## 2026-09-28 · T2 · Injected clock and repo-local EF tool
**Context:** Services set `CreatedAt` and `UpdatedAt`; migrations need `dotnet ef`.
**Decision:** Services take `TimeProvider` (registered as `TimeProvider.System`) instead of calling `DateTime.UtcNow`. `dotnet-ef` 10.0.12 is pinned in the repo's tool manifest (`dotnet-tools.json`); run `dotnet tool restore` after cloning.
**Why:** Tests can control time where ordering matters, and the EF tool version matches EF Core on any machine.
