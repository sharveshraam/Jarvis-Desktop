# Architecture

## Goals

- Native Windows experience, responsive while network work runs asynchronously.
- Keep routine local commands independent of Gemini.
- Separate contracts, provider code, local persistence, Windows integration, and UI.
- Require explicit approval for file creation and other consequential changes.
- Store user data locally and protect provider credentials with current-user DPAPI.

## Projects

- `Jarvis.Core`: conversation and tool contracts, settings and mode models.
- `Jarvis.AI`: provider interface and Gemini REST implementation.
- `Jarvis.Infrastructure`: SQLite conversation store and DPAPI secret store.
- `Jarvis.Windows`: allowlisted process launch, folder operations and system facts.
- `Jarvis.Agent`: bounded persistent background project-job queue and runner.
- `Jarvis.Desktop`: WPF shell, view models, composition and user approvals.
- `Jarvis.Tests`: unit tests for fast-path parsing and tool validation.

## Request path

1. The UI immediately appends the user message and starts asynchronous handling.
2. `LocalCommandRouter` checks deterministic commands first (time/date, arithmetic, and an allowlisted app launch).
3. Unmatched requests go to the configured AI provider. Only the recent conversation window is sent.
4. Tool requests are parsed and validated locally; the model never receives shell access.
5. Actions that modify files are shown for approval before execution, then verified and reported.
6. User and assistant messages are persisted in SQLite under `%LOCALAPPDATA%\Jarvis`.
7. Approved project build/test requests enter a FIFO queue and run as fixed `dotnet build/test --no-restore` commands outside the chat handler. Progress and bounded output are persisted. Queued work is safe to resume; a task interrupted while running is marked and never replayed automatically.

## Decisions

| Area | Decision | Trade-off |
|---|---|---|
| Runtime | .NET 10 LTS + WPF | Supported Windows desktop stack; Windows-only UI |
| Local store | SQLite via Microsoft.Data.Sqlite | Small local database; no server |
| LLM | Gemini REST behind `IAiProvider` | Direct API, no provider SDK lock-in; cloud chat requires internet |
| Gemini model | User-configurable model ID in Settings; default `gemini-3.8-flash` | Current model catalog lists this as stable and supports function calling; account access and model availability can still vary. See [Google's model catalog](https://ai.google.dev/gemini-api/docs/models) |
| Secret storage | Current-user DPAPI via Windows API | Secrets are portable only within this Windows user profile |
| Local action model | Deterministic allowlist, no arbitrary shell | Safe and fast for common tasks; narrower than a general agent |

## Data boundaries

- Never commit API keys, local databases, transcripts, or machine-specific settings.
- No background microphone, screen, or clipboard capture.
- Cloud requests are explicit and should disclose what content is sent.
- Long-term memory is stored locally and is never automatically appended to Gemini context.
- Destructive or externally visible operations remain approval-gated.
- Project build/test jobs require approval before starting because build targets and tests can execute project-defined code.
- Background jobs are currently limited to one active operation and two fixed .NET workflows; pause/resume, priorities, other language toolchains, and multi-step checkpoint recovery remain open work.
