# Tiel

A private, single-user chat app for local LLMs. It runs entirely on your machine: a Blazor web app
(interactive server rendering, .NET 10) talks to a local [Ollama](https://ollama.com) server and keeps
projects, chats, messages and settings in SQLite. It binds to `localhost` only and sends nothing
anywhere except the Ollama endpoint you configure.

- Chat with streamed replies, stop a reply mid-stream, and retry one that failed or was stopped.
- Projects with instructions that apply to every chat in them; a built-in **General** project.
- Automatic chat titles, a model picker per chat, and a trimmed-context note when the history no longer fits.
- A Settings page for the Ollama URL, the default model and each model's context length.
- Light and dark theme following the OS; works at phone width.

The full specification is in [`docs/phase-0-spec.md`](docs/phase-0-spec.md), and choices made where it
was silent are in [`docs/decisions.md`](docs/decisions.md).

## Prerequisites

- **.NET 10 SDK**: <https://dotnet.microsoft.com/download>. Check with `dotnet --version` (10.x). Node.js is not needed.
- **Ollama**, running and reachable at `http://localhost:11434` (the default).
- At least one chat model, for example:

  ```bash
  ollama pull phi4-mini
  ollama pull llama3.2:3b
  ```

  Small models (2B to 4B parameters) suit a 4 GB GPU. Thinking models such as `qwen3.5:4b` also work,
  but they reason silently before answering, so replies take longer.

Works on Linux, macOS and Windows.

## Run in development

```bash
dotnet run --project src/Tiel.Web
```

Then open <http://localhost:5080>. At startup the app creates the database if needed and syncs the
model list from Ollama. It starts fine without Ollama; the dot next to the app name turns green once
Ollama is reachable, and **Settings → Sync from Ollama** picks up new models.

Other commands:

```bash
dotnet build
dotnet test                 # no running Ollama needed; tests use fakes
dotnet watch --project src/Tiel.Web
dotnet tool restore         # once per clone, for `dotnet ef`
dotnet ef migrations add <Name> --project src/Tiel.Web
```

## Publish and run

```bash
dotnet publish src/Tiel.Web -c Release -o publish
```

This produces a self-contained folder for the OS you publish on; the target machine needs neither the
.NET SDK nor the runtime. Run it from anywhere:

```bash
./publish/Tiel.Web            # Linux and macOS
publish\Tiel.Web.exe          # Windows
```

It serves the app at <http://localhost:5080>. Stop it with Ctrl+C; replies that are still generating
are saved as stopped.

## Configuration

`appsettings.json` (next to the executable once published) holds the values that need a restart:

| Key | Default | Purpose |
| --- | --- | --- |
| `Urls` | `http://localhost:5080` | Where the app listens. Keep it on `localhost`: there is no authentication. |
| `Ollama:BaseUrl` | `http://localhost:11434` | The Ollama URL used on first run. After that, change it in **Settings**, which takes effect without a restart. |
| `ConnectionStrings:Tiel` | `Data Source={LocalApplicationData}/Tiel/tiel.db` | The SQLite database. |

Each can also be set with an environment variable, for example `Urls=http://localhost:5090` or
`ConnectionStrings__Tiel="Data Source=/path/to/chat.db"`.

## Where the data lives

Everything is in one SQLite database (with `-wal` and `-shm` files beside it while the app runs):

| OS | Location |
| --- | --- |
| Linux | `~/.local/share/Tiel/tiel.db` |
| macOS | `~/Library/Application Support/Tiel/tiel.db` |
| Windows | `%LOCALAPPDATA%\Tiel\tiel.db` |

Back it up by copying the file while the app is stopped.

### Reset

Stop the app, then delete the database files:

```bash
rm ~/.local/share/Tiel/tiel.db*                          # Linux
rm ~/Library/Application\ Support/Tiel/tiel.db*          # macOS
```

```powershell
Remove-Item "$env:LOCALAPPDATA\Tiel\tiel.db*"             # Windows
```

The next start creates an empty database with the General project and syncs the models again.

## Project layout

```
docs/                  spec and decisions log
src/Tiel.Web/     the app: Components (UI), Services (all rules), Data (EF Core, migrations, seeding)
tests/Tiel.Web.Tests/  service tests on temporary SQLite files, bUnit component tests, fakes for Ollama
```
