
# sharpRPA
# Please note this project is currently in an Alpha (preview) state and subject to change.
sharpRPA is a Free, Open-Source, and Easy-To-Use RPA (Robotic Process Automation) solution built on .NET 10 in C#.  sharpRPA allows you to automate straight-forward and repetative actions with ease - Don't Write Code, Build Functionality!

# Build
Requires the .NET 10 SDK on Windows (or `EnableWindowsTargeting` for cross-platform builds).

```
dotnet build sharpRPA.sln
```

Browser automation uses Microsoft Playwright. Install the Chromium browser before running browser commands:

```
pwsh sharpRPA/bin/Debug/net10.0-windows10.0.19041.0/playwright.ps1 install chromium
```

# Projects

| Project | Target | Purpose |
| --- | --- | --- |
| `sharpRPA.Core` | `net10.0` | UI-free engine: script model, `ScriptRunner`, cross-platform commands, recording lifecycle, SQLite storage, catalog/run/target services |
| `sharpRPA.Windows` | `net10.0-windows` | Windows-only commands: Win32, UI Automation, Excel, OCR, screenshot, clipboard |
| `sharpRPA` | `net10.0-windows` | WinForms script builder and debug runner |
| `sharpRPA.Mcp` | `net10.0` + `net10.0-windows` | MCP stdio server for AI agents |
| `sharpRPA.Tests` | `net10.0` | xUnit tests, runs on any OS |

# Tests

```
dotnet test sharpRPA.sln
```

The suite covers script execution, validation, command discovery, script
serialization against the shipped sample scripts, the recording lifecycle
(staging, commit, crash recovery) and the derived target index, and drives the
real MCP server as a child process over JSON-RPC.

# Storage

Scripts stay as XML files - they are code, so they remain copyable, diffable
and readable by agents. Everything operational lives in one SQLite database
(`Documents/sharpRPA/sharprpa.db`, override with `SHARPRPA_DB`):

| Data | Where | Why |
| --- | --- | --- |
| Scripts | `.xml` files | portable, git-friendly, hand-editable |
| Recording events | staging file while capturing, SQLite row on stop | a crash mid-capture never leaves a partial row |
| Target index | SQLite (`target_index`) | derived from scripts + recordings, rebuildable |
| Run history | in-memory (`ScriptRunManager`) | still volatile - persistence is the next increment |

Recording lifecycle: `record_start` opens a JSONL staging file under
`recording-staging/`, `record_event` appends, `record_stop` flushes to disk,
gzips the events into the `recording` row, refreshes the target index and only
then deletes the staging file. On startup the server reconciles orphans:
a staging file with no row is imported as `incomplete`, a row with a leftover
file just drops the file.

The target index (`file`, `url`, `window`, `ui_element`) is read-only derived
data - never a source of truth - so it can be rebuilt at any time with
`rebuild_target_index`.

# MCP Server

`sharpRPA.Mcp` exposes scripts, the command catalogue, headless execution,
recording and the target index to AI agents over MCP (stdio).

```
# cross-platform commands only (browser, HTTP, variables, flow control)
dotnet run --project sharpRPA.Mcp -f net10.0

# full command set including window/UI Automation/Excel/OCR (Windows)
dotnet run --project sharpRPA.Mcp -f net10.0-windows10.0.19041.0
```

Tools:

| Group | Tools |
| --- | --- |
| Scripts | `list_scripts`, `get_script`, `validate_script` |
| Commands | `list_commands`, `describe_command` |
| Execution | `run_script`, `get_run_status`, `get_run_log`, `cancel_run` |
| Recording | `record_start`, `record_event`, `record_stop`, `record_abort`, `list_recordings`, `get_recording` |
| Targets | `find_targets`, `rebuild_target_index` |

Environment variables:

- `SHARPRPA_SCRIPT_DIR` — script folder to serve (defaults to `Documents\sharpRPA\My Scripts`).
- `SHARPRPA_DB` — SQLite database path (defaults to `Documents\sharpRPA\sharprpa.db`).

Register it in opencode:

```json
{
  "$schema": "https://opencode.ai/config.json",
  "mcp": {
    "sharprpa": {
      "type": "local",
      "command": ["dotnet", "run", "--project", "sharpRPA.Mcp", "-f", "net10.0"],
      "enabled": true
    }
  }
}
```

Restart opencode after saving the config.

# Start Screen
![Main App Window](https://i.imgur.com/GkYEivM.png)

# Finished Script Examples
## Here are a few examples of sample scripts that have been built.
![Script Example1](https://i.imgur.com/j5HRt20.png)
![Script Example2](https://i.imgur.com/krFU9M1.png)


### Request a Feature
Don't see an automation feature you are looking for?  [Create a new enhancement issue!](https://github.com/saucepleez/sharpRPA/issues/new)

### Don't want to build your own?
Click [here](https://github.com/saucepleez/sharpRPA/releases) to view the latest releases.  Release binaries are built in Visual Studio 'Release' mode and packaged into a .ZIP file.  Simply extract to a folder, double-click and run to get started.
