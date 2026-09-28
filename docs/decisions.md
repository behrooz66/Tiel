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

## 2026-09-28 · T3 · OllamaSharp's source generator is removed from the build
**Context:** OllamaSharp 5.4.30 ships a source generator built for a newer compiler than SDK 10.0.1xx has. The result is warning CS9057, an error here (warnings as errors). `ExcludeAssets="analyzers"` does not remove it.
**Decision:** `Directory.Build.targets` removes OllamaSharp's analyzer items before `CoreCompile` in every project. `Microsoft.Extensions.AI` 10.10 is referenced directly (OllamaSharp only pulls 10.8).
**Why:** The generator only serves `[OllamaTool]` tool calling, which phase 0 excludes. The step is harmless on newer SDKs and can be dropped once the SDK catches up.

## 2026-09-28 · T3 · What "treats a missing or unavailable default model as unset" means
**Context:** `GetAsync` is "a typed view of all known keys", and values are cached until a write, but model availability changes during sync without a settings write.
**Decision:** `GetAsync` returns the stored `Chat.DefaultModelId` as parsed and does not check the Models table. Whoever uses the id treats a missing or unavailable model as unset: sync step 5 repairs it, conversation creation falls through to the first available model, and pickers only list available models. `SetDefaultModelAsync` rejects an unknown or unavailable id with `ValidationException` (as "switching to an unknown or unavailable model" does), keyed `DefaultModelId`.
**Why:** The cache stays a plain cache of the table, and a user's chosen default survives a model disappearing and coming back.

## 2026-09-28 · T3 · Ollama client lifetime and URLs
**Context:** The spec asks for clients cached per URL and reset on `SettingsChanged`, with no overall timeout on streaming.
**Decision:** One `OllamaApiClient` per URL serves as both `IChatClient` and `IOllamaApiClient`. All clients share one `SocketsHttpHandler` with a 10-second connect timeout, and `HttpClient.Timeout` is infinite; callers bound calls with their token. URLs are stored trimmed and without trailing slashes; the client adds one slash, so a path prefix such as `http://proxy/ollama` works. Tests reach the real provider through an internal constructor that takes an `HttpMessageHandler`.
**Why:** A dropped client holds no sockets, so the old one needs no disposal while a reply may still be streaming through it. The connect timeout stops a wrong address from hanging a generation forever without capping the stream.

## 2026-09-28 · T3 · Health and validation details
**Context:** The spec gives the DTOs, not the error shapes.
**Decision:** `GET /api/health` always returns 200 with `HealthStatus`; reachability is in the body. Failed checks are logged at Debug, since the sidebar polls every 30 seconds. `TestAsync` reports an invalid URL as a failed test with the validation message instead of throwing, and a server that answers with something other than Ollama's JSON is reported as "did not answer like Ollama". `ValidationException.Errors` maps field names (the DTO property names) to one message each.
**Why:** Test connection shows one error text either way, and the endpoint stays simple for scripts.

## 2026-09-28 · T3 · Model sync on URL change arrives in T4
**Context:** `SetOllamaBaseUrlAsync` runs a model sync, but the sync is T4's work.
**Decision:** In the T3 commit it validates, saves and raises `SettingsChanged` only; T4 adds the sync.
**Why:** One task per commit.

## 2026-09-28 · T4 · Sync semantics
**Context:** The spec gives the algorithm but not failure handling, timeouts or what "updated" counts.
**Decision:** A sync is all or nothing: any failed or timed-out Ollama call throws `OllamaUnavailableException` before anything is written, so an unreachable Ollama never marks models unavailable. One sync's Ollama calls share a 30-second timeout, and syncs run one at a time. `SyncResult.Updated` counts existing models whose availability or maximum context actually changed; an unchanged model is not counted and keeps its `UpdatedAt`. The maximum context is read from `<architecture>.context_length` when present, else from the first model-info key ending in `.context_length`. A new model's display name is its tag, cut to 100 characters.
**Why:** No partial state to reason about, and the summary shows what really changed.

## 2026-09-28 · T4 · Where syncs run and what a failure does
**Context:** Sync runs at startup, from Settings, and after saving the Ollama URL; the settings service and the sync depend on each other.
**Decision:** `SettingsService` takes `Lazy<IModelSyncService>` to break the dependency cycle. `SetOllamaBaseUrlAsync` keeps the saved URL when the sync that follows fails, and logs a warning instead of throwing. At startup a failed sync is logged as a warning and the app starts anyway.
**Why:** Pointing Settings at a wrong URL must work (the health indicator and Test connection report it), and the app must start without Ollama.

## 2026-09-28 · T4 · Model ordering and updates
**Context:** Models are listed, and the default is picked, "by display name".
**Decision:** Display-name order ignores case (`COLLATE NOCASE`) and breaks ties by tag. In `ModelService.UpdateAsync` a null argument leaves that field unchanged; a display name is trimmed and must be 1 to 100 characters. All field errors are reported together, and an update that changes nothing does not bump `UpdatedAt`.
**Why:** Matches what a person expects from an alphabetical list, and the Settings table can show every field error at once.

## 2026-09-28 · T5 · Project writes and events
**Context:** The spec lists the rules, not the text handling or event details.
**Decision:** Names are trimmed. Description and instructions are trimmed, and blank ones are stored as null. An update that changes nothing is not saved and raises no event. A project's `UpdatedAt` changes only when the project itself is edited, not when its conversations change. Deleting a project raises `ProjectsChanged` and `ConversationsChanged(projectId)`. A unique-name violation from a concurrent write becomes the same `ConflictException` as the up-front check. Stopping active generations before a delete is wired in T8, when `GenerationService` exists.
**Why:** The Projects page's "last updated" then means the project's settings, and every tab refreshes both lists after a delete.

## 2026-09-28 · T5 · Change events never fail a write
**Context:** `ChangeNotifier` handlers belong to UI components and run on the writer's thread.
**Decision:** Each handler is invoked separately; an exception is logged and the other handlers still run.
**Why:** A broken component must not turn a successful save into an error.

## 2026-09-28 · T6 · Conversation rules
**Context:** "The given one, else the default, else the first available" leaves open what happens when the given model is unavailable.
**Decision:** A given model must exist and be available, or `CreateAsync` throws `ValidationException`. The fallbacks apply only when no model is given. Every model rule error is keyed `ModelId`. Lists sort by `UpdatedAt` newest first, then by id (time-ordered v7), so ties are stable. A rename, model change or system-prompt change that changes nothing is not saved and raises no event. `ConversationDetail.IsGenerating` and stopping a generation before a delete are wired in T8.
**Why:** Silently swapping the model a user picked would be surprising; the other choices keep lists stable and events meaningful.

## 2026-09-28 · T7 · Prompt builder inputs and edge cases
**Context:** The spec defines the assembly and trimming rules but not the builder's inputs or a few edge cases.
**Decision:** `PromptBuilder.Build` takes the project instructions, the conversation's system prompt, all messages in sequence order and the context length. The last user message is the one being answered, and anything after it (the new streaming reply) is ignored, so send and retry share one path. Instructions and system prompt are trimmed before joining, and a whitespace-only message counts as empty. The result reports the dropped count, the estimate and the budget; `ExceedsBudget` tells the caller to log the warning, keeping the builder pure.
**Why:** One input shape covers both turn types, and logging stays out of the pure function.

## 2026-09-28 · T8 · A generation is released when its reply settles, before the title
**Context:** The lifecycle removes the generation from the registry in the final `finally`, after title generation, which can take up to 20 seconds. Until then `IsGenerating` stays true and a new send throws `ConflictException`.
**Decision:** The registry entry is removed as soon as the reply is saved and `GenerationCompleted` (or `GenerationFailed`) is published, still in a `finally`. The subscriber channels stay open until title generation ends, so `TitleGenerated` still reaches them, and they are completed in the outer `finally`. The events and their order are unchanged. The title is saved only if the chat is still called `New chat` at that moment, and the rename bumps `UpdatedAt`.
**Why:** The user can send the next message as soon as the answer is complete; a slow title call never blocks the chat.

## 2026-09-28 · T8 · Shutdown, stop and failure details
**Context:** The spec gives the outcomes, not the mechanics.
**Decision:** `ApplicationStopping` cancels every generation (and any title call). `GenerationService` is also a hosted service whose `StopAsync` waits for the cancelled replies to save. After a stop, any exception from the stream counts as the stop, since a cut connection can surface as an I/O error. A failed reply keeps the text it streamed; the bubble shows the `ErrorMessage`: "Ollama is unreachable: …", "Ollama returned an error: …", or a generic line with details in the log. Deleting a conversation or project stops its generations first; their final saves then update nothing. `SendAsync` stores the message trimmed and raises `ConversationsChanged` right away, since `UpdatedAt` moved. A retried reply takes the next free sequence number, which is the replaced reply's number.
**Why:** Each reply ends in a definite state the UI can show, even at shutdown.

## 2026-09-28 · T8 · `num_ctx`, and the title call uses the same context size
**Context:** The spec asks to verify how OllamaSharp's `IChatClient` takes Ollama options.
**Decision:** `ChatOptions.AddOllamaOption(OllamaOption.NumCtx, contextLength)` from OllamaSharp. A test records the `/api/chat` body and checks `options.num_ctx`. The title call sends the same `num_ctx` as the chat.
**Why:** Ollama reloads a model when `num_ctx` changes; on a 4 GB card that would add seconds to every title and to the next reply.
