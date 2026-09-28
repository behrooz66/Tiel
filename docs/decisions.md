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
