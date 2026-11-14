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
| Gemini provider and protected key | Streaming provider implemented; no live API test because no key was configured |
| Local conversation persistence | SQLite history tested |
| Gemini key protection | Windows current-user DPAPI round-trip tested |
| Safe local command fast path | Time/date, arithmetic, and an allowlisted app launcher; tested |
| Permission prompts for file changes | Not implemented yet |
| Tray, startup, global shortcut | Planned |
| Speech recognition and TTS | Not implemented; no benchmark claimed |
| Reminders, memory, background tasks | Planned |
| Windows build workflow | Added; portable ZIP artifact |
| Installer | Not implemented yet |

## Verification completed

- .NET 10.0.404 Release solution build: 0 warnings, 0 errors.
- Automated tests: 7 passed, 0 failed (fast-path routing, SQLite history, DPAPI secret round-trip).
- Published and opened a self-contained `win-x64` single-file build.
- Live UI smoke check: `calculate 17 * 23` returned `391` with the `Completed locally` status.
- Gemini network streaming was not tested against a real account because no API key was configured.

## Release notes

The current build is an early development milestone, not a complete release. It still needs the permission-gated file tools, tray/shortcut support, voice pipeline and evaluation, reminders/memory, broader failure tests, and an installer before release claims are appropriate.
