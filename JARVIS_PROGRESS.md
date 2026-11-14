# JARVIS Progress

## Baseline audit

- This workspace was empty and was not a Git checkout.
- The current desktop app is installed separately; no source code was present in this workspace.
- The existing GitHub repositories remain untouched. A new public repository, `sharveshraam/Jarvis-Desktop`, was created per the current request.
- The target branch is `feature/jarvis-desktop`.
- The machine had .NET 8 runtime but no .NET SDK at audit time.

## Status

| Capability | Status |
|---|---|
| WPF app shell and chat | Working foundation; modes and cancel are wired |
| System tray | Implemented: close hides the window, tray double-click/menu reopens, explicit Quit exits |
| Startup and global shortcut | Settings include optional Windows sign-in launch and configurable global key chord; Ctrl+Alt+J was occupied during smoke check, default changed to Ctrl+Shift+F12; new default registration still needs live verification |
| Gemini provider and protected key | Streaming provider, function declarations, structured tool-call parsing, and protected key implemented; live API not tested because no key was configured |
| Model configuration | Gemini model ID is editable in Settings and applied per request; default ID checked against Google's current stable model catalog; real account availability remains unverified |
| Agent execution | Bounded to three model/tool rounds and six local calls per request; allowlisted tools validate arguments; file writes require approval and Gemini-requested file reads require explicit sharing approval |
| Project workspace | User-selected workspace; bounded source listing/search/read and approve-before-write previews; traversal, reparse points, build output, dependency folders, and unsupported extensions are excluded; source disclosure requires approval |
| Persistent memory | Explicit local `remember` command, list/search, and approval-gated forget; stored in SQLite with duplicate suppression; memory is not automatically attached to Gemini requests |
| Editable profile | Local JSON profile with user-editable name and context; included in Gemini system instruction; Settings discloses cloud transfer |
| Reminders | Relative minute/hour/day reminders persisted in SQLite, list command, and tray balloon notification polling; due records are marked delivered |
| Local conversation persistence | SQLite history tested |
| Gemini key protection | Windows current-user DPAPI round-trip tested |
| Safe local command fast path | Time/date, arithmetic, and allowlisted Notepad, Calculator, Paint, File Explorer, VS Code, and Visual Studio launch requests; tested without launching apps in tests |
| Permission prompts for file changes | Desktop folder creation asks before writing; path is restricted to one folder directly on Desktop; overwrite and traversal blocked; tested |
| Desktop text files | Approval-gated new .txt/.md creation; no overwrite; bounded text read for selected formats; traversal blocked; tests cover approve, decline, and read |
| System information | RAM usage read directly from Windows; local routing covered by a deterministic test |
| File discovery | Bounded local recursive PDF search across Desktop, Documents, and Downloads; skips junctions and reports when the search cap is reached |
| Speech recognition and TTS | Local System.Speech dictation button with review-before-send; optional read-aloud preference off by default; recognition and playback duration now measured locally; live microphone, voice quality and interruption checks pending |
| Background project jobs | First slice implemented: approved dotnet build/test jobs run off the UI thread; task state/progress/output persist in SQLite; queued jobs can resume after restart, while in-progress work is marked interrupted and never replayed; cancel and tray completion notifications are wired |
| Performance diagnostics | Local-only timing samples for local/Gemini turns and speech capture/output; reports p50/p90/p95 latencies, call counts and failures through `show JARVIS diagnostics`; telemetry excludes prompts, transcripts, API keys and tool payloads |
| Windows build workflow | Added; portable ZIP artifact |
| Installer | Not implemented yet |

## Verification completed

- .NET 10.0.404 Release solution build: 0 warnings, 0 errors.
- Automated tests: 46 passed, 0 failed (previous coverage plus content-free telemetry persistence, latency percentile aggregation, and local diagnostics routing).
- Latest full Release solution build: 0 warnings, 0 errors.
- An earlier development milestone was published and opened as a self-contained `win-x64` single-file build; latest structured-tool and speech changes still need a fresh runtime smoke check.
- Live UI smoke check: `calculate 17 * 23` returned `391` with the `Completed locally` status.
- A live Gemini probe was attempted after the user restored internet access, but the current Windows-user JARVIS key store has no API key; the API request was not sent. The live app UI was also unresponsive while the captured screen was black/locked.

## Release notes

The current build is an early development milestone, not a complete release. File support covers Desktop actions plus a selected project workspace with approval-gated source disclosure and file changes. Gemini tools can request these actions within a bounded call loop. Reminders support relative minute/hour/day times and tray notification. Local memory, background .NET build/test tasks, and content-free timing diagnostics are implemented. Voice quality, device behavior and the new tool loop against a live Gemini account have not been verified. It still needs broader file and app workflows, richer task recovery/control, system diagnostics, fuller performance and voice evaluation, and an installer before release claims are appropriate.
