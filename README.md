# JARVIS Desktop

An independent Windows desktop assistant for chat, quick local actions, and safe computer workflows.

## Current state

This repository is the new desktop application, separate from Lumi and the existing `Project-Jarvis` repository. Work is underway on `feature/jarvis-desktop`.

The current development build has a native WPF shell, persisted local conversations, a DPAPI-protected Gemini key, streamed Gemini chat, and a small allowlisted set of fast local commands. See [JARVIS_PROGRESS.md](JARVIS_PROGRESS.md) for verified behavior and remaining work.

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

## Privacy

Conversation history and settings live under `%LOCALAPPDATA%\Jarvis`. The Gemini API key is protected using Windows DPAPI. When cloud chat is used, the text of the current conversation is sent to Google Gemini. Local quick commands do not require network access. JARVIS does not record audio or capture the screen in the background.

## Project layout

See [ARCHITECTURE.md](ARCHITECTURE.md). This is an incremental project; unimplemented features are tracked explicitly rather than presented as working.
