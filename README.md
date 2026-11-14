# JARVIS Desktop

An independent Windows desktop assistant for chat, quick local actions, and safe computer workflows.

## Current state

This repository is the new desktop application, separate from Lumi and the existing `Project-Jarvis` repository. Work is underway on `feature/jarvis-desktop`.

The current development build has a native WPF shell, persisted local conversations and working mode, a DPAPI-protected Gemini key, streamed Gemini chat, bounded local file workflows, reminders, memory, project tools, and background build/test tasks. See [JARVIS_PROGRESS.md](JARVIS_PROGRESS.md) and [progress_update.md](progress_update.md) for verified behavior and remaining work.

## Requirements

- Windows 10/11 x64
- .NET 10 SDK (LTS)
- Gemini API key for cloud chat; local quick commands work without one

## Build

```powershell
dotnet restore
dotnet build Jarvis.sln -c Release
dotnet test Jarvis.sln -c Release
dotnet publish src/Jarvis.Desktop/Jarvis.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/publish
```

The published application contains no API key. Configure the key from JARVIS Settings; it is protected for the current Windows user with DPAPI.

## Project tasks

Select a project workspace in Settings, then ask JARVIS to `build project` or `test project`. These fixed .NET commands require confirmation and run in the background without restoring packages. Use `show project tasks` to list jobs, `show task <id>` for the output, and `cancel task <id>` to stop an active job. Running jobs interrupted by an app restart are recorded and are not rerun automatically. The current task runner supports .NET solutions and projects only.

## Performance diagnostics

Use `show JARVIS diagnostics` to view recent local-command, Gemini, speech recognition, and speech output timing percentiles and model/tool call counts. Measurements are kept locally and omit prompts, transcripts, API keys, and tool arguments.

## Local files and modes

Try `find files containing physics` to search filenames in Desktop, Documents, and Downloads. JARVIS skips linked folders, limits traversal and result counts, and returns paths so you can choose a file. When Gemini requests a local filename search or text-file read, JARVIS previews the names or file details and asks before sharing them. Supported text files are limited to 1 MB and to those three user folders; binary files and linked paths are excluded.

Choose Personal, Study, Development, Research, Focus, System, or Creator mode from the mode selector. Each mode has distinct response guidance, and JARVIS remembers your selection in local preferences.

## Privacy

Conversation history and settings live under `%LOCALAPPDATA%\Jarvis`. The Gemini API key is protected using Windows DPAPI. When cloud chat is used, the text of the current conversation is sent to Google Gemini. Local quick commands do not require network access. For cloud-requested file search, matching names and paths are shared only after approval; file contents are read and sent only after a separate approval. JARVIS does not record audio or capture the screen in the background.

## Project layout

See [ARCHITECTURE.md](ARCHITECTURE.md). This is an incremental project; unimplemented features are tracked explicitly rather than presented as working.
