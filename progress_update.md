# JARVIS Progress Update

Last updated: 2026-11-14

## Repository and branch

- Public repository: `sharveshraam/Jarvis-Desktop`
- Working branch: `feature/jarvis-desktop`
- Lumi and the original repositories remain separate and untouched.
- Latest feature commit: `b4bb802 feat: persist selected working mode`.
- Progress log is maintained alongside each implementation increment on this branch.
- Previous milestone: `d6fa3e3 feat: add persistent background project tasks`.

## Completed milestones

1. Created the independent .NET 10 WPF desktop assistant and a GitHub Actions Windows build/test/self-contained publish workflow.
2. Added the layered solution: `Jarvis.Core`, `Jarvis.AI`, `Jarvis.Infrastructure`, `Jarvis.Windows`, `Jarvis.Agent`, `Jarvis.Desktop`, and `Jarvis.Tests`.
3. Added SQLite conversation/reminder/memory/task persistence, current-user DPAPI key storage, user profile/preferences, and tray integration.
4. Added Gemini streaming and function calls behind an AI-provider interface, editable model selection, bounded tool execution, and cancellation/timeouts.
5. Added local fast-path commands for arithmetic, time/date, RAM, app launch allowlist, PDF discovery, reminders, and approved Desktop text/folder operations.
6. Added a selected project workspace with bounded source listing/search/read and previewed, approval-gated writes. Paths are constrained to the workspace and exclude link/reparse points, build outputs, and unsupported extensions.
7. Added explicit local memory commands (`remember`, list/search, approval-gated `forget`); memory is not automatically sent to Gemini.
8. Added a FIFO background .NET build/test task runner. Jobs require confirmation, store status/output, support cancellation and task inspection, notify on completion, resume only queued jobs after restart, and mark running jobs interrupted instead of replaying them.
9. Added push-to-talk Windows speech recognition with transcript review before sending and optional local speech output.
10. Added local content-free performance telemetry for local/Gemini interactions and voice capture/output; `show JARVIS diagnostics` reports p50/p90/p95 interaction latency, first-output latency, model/tool timing, call counts, and failures.

## Verification completed

- Latest verified test run: **68 passed, 0 failed**.
- Latest verified full Release build: **0 warnings, 0 errors**.
- GitHub Actions run [#18](https://github.com/sharveshraam/Jarvis-Desktop/actions/runs/43601058115) for `64fd6b7` completed successfully, including Windows restore, Release build, tests, self-contained `win-x64` publish, ZIP packaging, and artifact upload.
- Latest mode persistence code run `b4bb802` [completed successfully](https://github.com/sharveshraam/Jarvis-Desktop/actions/runs/43603130140); the follow-up docs run `6b75e62` [also completed successfully](https://github.com/sharveshraam/Jarvis-Desktop/actions/runs/43603158972).
- Live Gemini request was not sent: the current Windows-user key store has no API key configured.
- Live UI input could not be completed: JARVIS's window was present in the app inventory, but its captured screen was black and Windows rejected interaction while locked.
- No final signed release installer has been produced. GitHub Actions has built and uploaded a self-contained development ZIP artifact; the full requested project is still in progress.

## Latest increment

- Added explicit-consent Gemini tools for searching local filenames and reading supported text files in Desktop, Documents, and Downloads.
- Contents are not read until approval. Approval shows path, size, and modification time; changed files require fresh confirmation, and changes during reading are detected before content is returned. Access is bounded to 1 MB text files and refuses unsupported extensions, paths outside allowed roots, and reparse points.
- Verification: 68 tests pass and the full Release build has 0 warnings and 0 errors. README privacy guidance is updated; this increment is being prepared for push.

## Remaining work from the master build prompt

- Confirm the docs-only CI run completes; continue with broader file/app workflows and remaining release requirements.
- Broaden file and Windows application workflows and add stronger UI Automation integrations.
- Improve and benchmark speech recognition/TTS, including manual microphone and interruption tests; current voice implementation is a basic local baseline.
- Implement richer research/search integrations, context management, configurable routing beyond a single Gemini model, and broader software-development workflows.
- Expand background task controls/checkpoints, verification evidence, and non-.NET toolchains.
- Add detailed voice/provider state to diagnostics; local CPU, busiest GPU-engine, RAM, and network diagnostics are implemented.
- Complete security/privacy and performance evaluations, run acceptance tests, and conduct a live Gemini test after a key is configured.
- Create and verify a polished installer and final self-contained Windows executable after the project is sufficiently complete.

## Update policy

Append a dated entry here whenever a meaningful project increment is completed. Record the change, the exact test/build result, and whether it has been pushed; do not mark planned work as done.

## 2026-11-14 increment — local system status

- Added a local system status command reporting sampled CPU utilization, logical processor count, RAM use, and cumulative receive/send traffic across active non-loopback network adapters.
- GPU utilization is explicitly reported as unavailable; no estimate or fabricated value is shown.
- Added a router test covering the status response. Verification: **47 tests passed, 0 failed**; full Release solution build succeeded with **0 warnings and 0 errors**.
- Pushed as `b5e306e feat: add local CPU and network diagnostics` to `origin/feature/jarvis-desktop`.

## 2026-11-14 increment — GPU engine diagnostics

- Added Windows Performance Data Helper sampling for the busiest `GPU Engine` utilization counter. JARVIS labels this as busiest-engine utilization rather than presenting it as an aggregate across all GPU engines.
- The value is optional: when Windows or the installed driver does not expose GPU engine counters, the system-status response says GPU data is unavailable.
- Added an end-to-end Windows system-counter test and expanded the status-router test. Verification: **48 tests passed, 0 failed**; full Release solution build succeeded with **0 warnings and 0 errors**.
- Pushed as `45dadeb feat: report busiest GPU engine utilization` to `origin/feature/jarvis-desktop`.

## 2026-11-14 increment — reminder cancellation

- Added persistent cancellation for upcoming reminders by short ID or exact title; reminder listings now include a stable short ID, and ambiguous titles ask the user to choose by ID.
- Canceled reminders are filtered out of upcoming and due notifications. Existing databases are migrated in place by adding a `canceled` column without dropping or rewriting saved reminders.
- Added coverage for cancellation, due-list suppression, ambiguous title handling, ID listing, and migration from the existing schema. Verification: **51 tests passed, 0 failed**; full Release solution build succeeded with **0 warnings and 0 errors**.
- Pushed as `75579df feat: add persistent reminder cancellation` to `origin/feature/jarvis-desktop`.

## 2026-11-14 increment — bounded filename search

- Added local partial-name search for files in Desktop, Documents, and Downloads, with newest-first results and full file paths.
- Search skips reparse points, rejects path/wildcard input, limits traversal to 4,000 directories and results to 10, and reports when those bounds truncate results.
- Verification: **53 tests passed, 0 failed**; full Release solution build succeeded with **0 warnings and 0 errors**.
- Pushed as `a3603ae feat: add bounded local filename search` to `origin/feature/jarvis-desktop`. GitHub Actions run [#21](https://github.com/sharveshraam/Jarvis-Desktop/actions/runs/43602640452) and docs run [#22](https://github.com/sharveshraam/Jarvis-Desktop/actions/runs/43602669568) both completed successfully.

## 2026-11-14 increment — working mode behavior

- Implemented mode-specific response guidance for all seven working modes and added provider-level tests confirming the selected instructions are sent to Gemini.
- Verification: **60 tests passed, 0 failed**; full Release solution build succeeded with **0 warnings and 0 errors**.
- Pushed as `9d17fda feat: tailor responses to working modes` to `origin/feature/jarvis-desktop`.

## 2026-11-14 increment — persist selected working mode

- Added the selected JARVIS mode to local user preferences and load it at startup; changing modes saves the selection while preserving other settings.
- Older preference files without a mode continue to load as Personal. Unknown numeric enum values fall back to Personal, and invalid mode values cannot be saved.
- Added tests for readable mode persistence and legacy settings compatibility. Verification: **62 tests passed, 0 failed**; full Release solution build succeeded with **0 warnings and 0 errors**.
- Pushed as `b4bb802 feat: persist selected working mode` to `origin/feature/jarvis-desktop`.

## 2026-11-14 increment — approval-gated local text files

- Added Gemini tools to search user filenames and read supported local text files from Desktop, Documents, or Downloads.
- Requires a preview and explicit approval before sending names/paths, and separate approval before sending contents. Enforces folder boundaries, extension allowlists, a 1 MB size cap, reparse-point exclusion, strict UTF-8, and file-change detection before and after the read.
- Added tests for declined and approved searches, declined and approved reads, off-folder rejection, and change-after-approval detection. Verification: **68 tests passed, 0 failed**; full Release solution build succeeded with **0 warnings and 0 errors**.
- GitHub Actions for mode persistence [passed](https://github.com/sharveshraam/Jarvis-Desktop/actions/runs/43603130140) and its progress-log push [passed](https://github.com/sharveshraam/Jarvis-Desktop/actions/runs/43603158972). This increment is ready to push.
