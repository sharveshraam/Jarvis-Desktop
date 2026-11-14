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
| Startup and global shortcut | Settings include optional Windows sign-in launch and configurable global key chord; runtime smoke found Ctrl+Alt+J occupied, so default changed to Ctrl+Shift+F12 for verification |
| Gemini provider and protected key | Streaming provider implemented; no live API test because no key was configured |
| Editable profile | Local JSON profile with user-editable name and context; included in Gemini system instruction; Settings discloses cloud transfer |
| Reminders | Relative minute/hour/day reminders persisted in SQLite, list command, and tray balloon notification polling; due records are marked delivered |
| Local conversation persistence | SQLite history tested |
| Gemini key protection | Windows current-user DPAPI round-trip tested |
| Safe local command fast path | Time/date, arithmetic, and allowlisted Notepad, Calculator, Paint, File Explorer, VS Code, and Visual Studio launch requests; tested without launching apps in tests |
| Permission prompts for file changes | Desktop folder creation asks before writing; path is restricted to one folder directly on Desktop; overwrite and traversal blocked; tested |
| Desktop text files | Approval-gated new .txt/.md creation; no overwrite; bounded text read for selected formats; traversal blocked; tests cover approve, decline, and read |
| System information | RAM usage read directly from Windows; local routing covered by a deterministic test |
| File discovery | Bounded local recursive PDF search across Desktop, Documents, and Downloads; skips junctions and reports when the search cap is reached |
| Startup and global shortcut | Planned |
| Speech recognition and TTS | Local System.Speech dictation button with review-before-send; optional read-aloud preference off by default; live microphone and voice checks pending |
| Reminders, memory, background tasks | Planned |
| Windows build workflow | Added; portable ZIP artifact |
| Installer | Not implemented yet |

## Verification completed

- .NET 10.0.404 Release solution build: 0 warnings, 0 errors.
- Automated tests: 25 passed, 0 failed (fast-path routing, allowlisted development-app launch, approved/canceled/rejected Desktop file actions, local RAM reporting, newest-PDF search, SQLite conversation and reminder persistence/delivery, DPAPI secret round-trip, user profile and preference persistence/validation, Gemini SSE/profile/key placement).
- Published and opened a self-contained `win-x64` single-file build.
- Live UI smoke check: `calculate 17 * 23` returned `391` with the `Completed locally` status.
- Gemini network streaming was not tested against a real account because no API key was configured.

## Release notes

The current build is an early development milestone, not a complete release. File support currently covers Desktop folder creation, creating new .txt/.md files with approval, reading bounded text files, and locating the latest PDF across standard user folders. Reminders currently support relative minute/hour/day times and a tray notification. Voice APIs are wired, but microphone/device behavior and speech latency have not been verified on this locked desktop. It still needs broader file tools, persistent memory, richer workflows, broader failure tests, and an installer before release claims are appropriate.
