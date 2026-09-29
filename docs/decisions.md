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
**Decision:** `git init` in this folder (it plays the role of `tiel/` in the layout), with the spec moved to `docs/phase-0-spec.md` and this log to `docs/decisions.md`, committed as `T0`.
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

## 2026-09-28 · T9 · Theme, icons and assets
**Context:** The spec asks for Fluent UI's OS-following theme, a vendored highlight.js and icon-only buttons with labels.
**Decision:** Fluent UI v5 follows the OS theme by default and sets `body[data-theme="dark"]`; its default stylesheet colors `body`. The app's CSS uses the same design tokens (`--colorNeutralBackground1` and so on), plus Fluent's recommended inline style against a white flash in dark mode. highlight.js 11.12.0 (the `@highlightjs/cdn-assets` build, checked against npm's sha512) is vendored with its GitHub light and dark styles, picked by `prefers-color-scheme`. Icons are a small in-repo SVG component instead of the Fluent icons package.
**Why:** No theme code or runtime downloads; a few simple icons don't justify a multi-megabyte package.

## 2026-09-28 · T9 · Errors, toasts and JavaScript
**Context:** "Service exceptions surface as toasts; an ErrorBoundary catches anything unexpected."
**Decision:** Components catch only the four typed service exceptions (`IsServiceError()`) and show them with `INotificationService` error toasts. Anything else reaches Blazor's `ErrorBoundary` around the main area, which logs it and shows a calm panel with Try again; navigating also recovers. `wwwroot/js/interop.js` is one ES module with exactly the four allowed concerns. It inserts the code toolbar inside each `<pre>` so the nodes Blazor rendered stay in place. The reconnect dialog keeps the template's mechanics with shorter wording, and says that replies keep generating on the server.
**Why:** Expected failures get a clear message; bugs aren't hidden behind a toast.

## 2026-09-28 · T10 · Remembering the last project, and confirmations
**Context:** `/` must reopen "the last-used project (remembered per browser)", and our JavaScript is limited to four uses.
**Decision:** A scoped `ProjectContext` holds the sidebar's project for each tab and remembers it with ASP.NET Core's `ProtectedLocalStorage`, framework code rather than ours in `interop.js`. An unreadable or stale value falls back to General. Confirmations use Fluent's `IDialogService.ShowConfirmationAsync` instead of a custom `ConfirmDialog` component. The delete message reads "…and its 1 conversation" in the singular.
**Why:** Per-browser memory with no extra JavaScript, and one fewer component to maintain.

## 2026-09-28 · T10 · Sidebar and dialog details
**Context:** Details the spec leaves to the implementation.
**Decision:** One shared Fluent menu (Rename, Delete) opens next to the row's "…" button. Deleting a chat also asks for confirmation. Deleting the chat you are viewing goes to its project's new-chat page; when the current project disappears (here or in another tab), the sidebar switches to General. Relative times are "now", "5m", "3h", "2d", then a date; they refresh every minute. The project dialog passes its own `MessageCondition` to Fluent's inputs, because the default condition replaces any message with "This field is required". `NewChat` exists as a stub until T11 fills it in.
**Why:** Keeps the sidebar consistent with the data in every tab, and works around a Fluent v5 behavior that hid our field errors.

## 2026-09-28 · T11 · Rendering model output safely
**Context:** Markdig with advanced extensions and `DisableHtml()`, with links in a new tab. Model output is untrusted text.
**Decision:** Besides escaping raw HTML, `MarkdownRenderer` turns images into plain links and replaces any link target other than http, https or mailto with `#`. The code toolbar (language and Copy) is added by `interop.js` after a reply is final, inside each `<pre>`; a failed copy shows "Couldn't copy".
**Why:** An image in a reply would make the browser fetch a URL of the model's choosing, breaking "send nothing anywhere"; a `javascript:` link would run in the app's origin.

## 2026-09-28 · T11 · Chat view mechanics
**Context:** How the live view, composer and messages behave in detail.
**Decision:**
- The chat page subscribes after loading. It also re-checks on `ConversationsChanged`, which follows renames and deletes from other tabs and picks up a reply started in another tab.
- Deltas re-render through a throttle (at most every 50 ms), and messages whose parameters didn't change skip rendering.
- Each assistant message shows the display name of the model that wrote it.
- The composer is a plain `<textarea>` that grows with CSS `field-sizing: content`, up to about 10 lines, where the browser supports it; elsewhere it scrolls. Send and Stop are icon buttons with `aria-label`s.
- The per-chat system prompt has a service method but no UI, because the spec's UI doesn't include an editor.
**Why:** Stays within the four allowed JavaScript uses and keeps streaming renders cheap.

## 2026-09-28 · T11 · Fixes found by running against real Ollama
**Context:** End-to-end runs with phi4-mini and qwen3.5:4b turned up behaviors the fakes didn't have.
**Decision:**
- When the token is cancelled, OllamaSharp's stream ends quietly instead of throwing. `GenerationService` now checks the token after the stream, so a stop is saved as `Cancelled` rather than as a short `Complete` reply that then gets a title. A fake mode that ends quietly on cancel covers it.
- A small model sometimes answers the title prompt with a code block. The title cleaner now treats a first line starting with ```` ``` ```` as no title, so the fallback applies.
- An HTTP error status from Ollama no longer reads "Ollama is unreachable". A 404 says the model isn't installed; other statuses show the code.
**Why:** Each was a visible wrong result in the real app.

## 2026-09-28 · T12 · No FluentNumberInput, and a guard against CDN-loading components
**Context:** A browser run of the Settings page showed a request to `https://unpkg.com/imask@7.6.1/dist/imask.min.js`: Fluent's `FluentNumberInput` lazy-loads its input-mask library from unpkg as soon as it renders.
**Decision:** Context length uses a `FluentTextInput` of type Number, and the page parses a whole number before saving ("Enter a whole number." otherwise). `NoCdnTests` fails when a Razor file uses `FluentNumberInput`, `FluentSortableList` or `MaskPattern` (the components that fetch from unpkg), or links a script or stylesheet on another host. Browser runs of every page now report no host other than localhost.
**Why:** "Send nothing anywhere except the configured Ollama endpoint" is a hard constraint, and the leak came from a library default rather than our code, so it needs a test to stay fixed.

## 2026-09-28 · T12 · Settings page behavior
**Context:** Details the spec leaves open.
**Decision:** Test connection checks the typed URL without saving. Save stores it, runs the sync through `SetOllamaBaseUrlAsync`, then checks health once to say whether models were synced or Ollama isn't reachable there. The health indicator re-checks at once through `SettingsChanged`. Each model row has its own Save, enabled only when the row changed, with field errors inline. The default-model picker lists available models only.
**Why:** Clear feedback for each action, and no half-saved rows.

## 2026-09-28 · T13 · Publishing and the content root
**Context:** `dotnet publish -c Release` must produce a self-contained folder for the current OS, and the published app must run from anywhere.
**Decision:** The web project sets `SelfContained` and `UseCurrentRuntimeIdentifier` only while publishing (`_IsPublishing`), so builds and tests keep the portable output. `Program.cs` sets the content root to `AppContext.BaseDirectory`, so a published build finds `appsettings.json` and `wwwroot` whatever the working directory; development runs are unaffected. The README documents `-o publish`, the database location on each OS, and how to reset it.
**Why:** The default content root is the working directory, so starting `./publish/Tiel.Web` from elsewhere lost its configuration and static files.

## 2026-09-28 · T8 fix · A reply is settled once its final event is out
**Context:** Bug report: after a reply finished, the composer kept showing Stop. The notification raised after completion comes before the generation is unregistered. A chat page refreshing on it saw `IsGenerating` true, subscribed again, and got a live snapshot of a reply whose final event had already gone out, so it waited forever. Fast models such as llama3.2:3b hit this almost every time.
**Decision:** Publishing the final event (completed or failed) marks the generation settled under the same lock `Subscribe` takes. From then on `Subscribe` returns no snapshot and `IsGenerating` returns false, even while title generation keeps the subscriber channels open.
**Why:** Subscribing is only meaningful while the final event is still to come; this closes the window in the service rather than in each caller.

## 2026-09-29 · Rename · LocalChat is now Tiel
**Context:** The app's working name was LocalChat; it is now Tiel.
**Decision:**
- Renamed with `git mv` so history follows the files: `Tiel.slnx`, `src/Tiel.Web`, `tests/Tiel.Web.Tests` and the `Tiel.Web` namespaces.
- The connection string key is `ConnectionStrings:Tiel`, and the default database is `{LocalApplicationData}/Tiel/tiel.db`.
- UI titles, the README, `CLAUDE.md` and the docs use the new name, including earlier entries in this log.
- The EF migration files had their namespaces and entity type names edited by hand, not regenerated. The migration id `20260928195049_InitialCreate` is unchanged, so existing databases apply nothing new.
- The old key `ConnectionStrings:LocalChat` is no longer read.
- The browser's remembered last project (now `tiel.lastProject`) starts over once, at General.
- The rename and the move of existing databases (next entry) are separate commits. Git links a renamed file to its history by content similarity, and a file rewritten in the same commit as its move would lose that link.

**Why:** A new name, with every file's history intact.

## 2026-09-29 · Rename · Existing databases move to the new location
**Context:** Chats made before the rename live in `{LocalApplicationData}/LocalChat/localchat.db`, and the app now opens `{LocalApplicationData}/Tiel/tiel.db`.
**Decision:** At startup, `SqliteConnectionStrings.Resolve` moves the old database to the new location, but only when:
- the configured database is the default location;
- there is no `tiel.db` there yet;
- `LocalChat/localchat.db` exists.

The `-wal`, `-shm` and `-journal` files move first and the database last, so an interrupted move completes on the next start. The emptied `LocalChat` folder is then removed, and the move is logged. A database configured anywhere else is never moved. The old app must be stopped first, since it would otherwise keep writing to the old path.
**Why:** Chats carry over without a manual step. Limiting the move to the default location means a test or custom path can never pick up the real database.
