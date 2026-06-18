# WinCodexBar

A small Windows system-tray app that shows AI coding usage/limits for **Claude**, **GitHub Copilot**,
and **OpenAI** — a Windows take on the macOS app [CodexBar](https://codexbar.app/).

Left-click the tray icon for a popover with usage bars and reset countdowns. Right-click for the menu
(Refresh, Copilot device login, edit settings, quit).

---

## Read this first — what is and isn't solid

The tray UI is the easy part. The hard part is getting usage data, because every provider exposes it
through **undocumented / reverse-engineered** surfaces that break periodically. CodexBar itself has open
bugs where the Claude session dies every ~8h (OAuth) or ~30 days (cookie). Expect the same fragility here.

| Provider | Path used | Confidence | Notes |
|---|---|---|---|
| Claude | `claude.ai` sessionKey cookie → `/api/organizations` + `/api/organizations/{id}/usage` | **High** on endpoints | Inner field names (`utilization`, `resets_at`) are parsed defensively — `// VERIFY` |
| OpenAI | Admin key → `/v1/organization/costs` (legacy credit fallback) | Medium | API only exposes **spend**, not subscription windows. `// VERIFY` |
| Copilot | GitHub token → `copilot_internal/user` | Medium | Unofficial endpoint. `// VERIFY` |

Lines marked `// VERIFY` in the source are the ones to check against a live response the first time you run.
Failures are handled gracefully: a broken provider shows an error in its card instead of crashing the app.

> I could not compile this in my environment (no .NET SDK there), so treat it as carefully-written but
> un-compiled. If the compiler flags anything, it'll be a small fix.

---

## Build & run

Requires the **.NET 8 SDK** (Windows). https://dotnet.microsoft.com/download

```powershell
cd WinCodexBar
dotnet run
```

A two-bar icon appears in the tray. Build a single distributable .exe:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# output: bin\Release\net8.0-windows\win-x64\publish\WinCodexBar.exe
```

To launch at login, drop a shortcut to the .exe in `shell:startup`.

---

## Credentials (Windows)

Config lives at `%APPDATA%\WinCodexBar\config.json` (auto-created on first run). Right-click the tray
icon → **Edit settings** to open it.

**Claude** — open `claude.ai` in your browser, DevTools (F12) → Application → Cookies → `https://claude.ai`
→ copy the value of the `sessionKey` cookie (starts with `sk-ant-sid01-...`) into `SessionKey`.
It expires after ~30 days; you'll need to repaste it then. (An `sk-ant-admin...` key in `ApiKey` enables
the spend view instead.)

**Copilot** — easiest: right-click the tray icon → **Login to Copilot (device flow)**. Or paste a GitHub
token with Copilot access into `Token`, or set the `COPILOT_API_TOKEN` environment variable.

**OpenAI** — create an **Admin key** (`sk-admin-...`) at platform.openai.com → Settings → Admin keys, and
put it in `ApiKey` (or the `OPENAI_ADMIN_KEY` env var). A normal `sk-...` key only gets the legacy
credit-balance fallback, which most accounts no longer support.

Example `config.json`:

```json
{
  "RefreshSeconds": 300,
  "Providers": [
    { "Id": "claude",  "Enabled": true, "SessionKey": "sk-ant-sid01-..." },
    { "Id": "copilot", "Enabled": true, "Token": "gho_..." },
    { "Id": "openai",  "Enabled": true, "ApiKey": "sk-admin-..." }
  ]
}
```

Keep this file private — it holds secrets.

---

## Project layout

```
Program.cs              entry point
TrayAppContext.cs       tray icon, menu, refresh loop, popup wiring
Models.cs               UsageWindow / ProviderSnapshot
Config.cs               %APPDATA% config load/save
Providers/
  IUsageProvider.cs     interface + shared HttpClient
  ClaudeProvider.cs     claude.ai cookie path (+ admin stub)
  OpenAiProvider.cs     org costs (+ legacy credit fallback)
  CopilotProvider.cs    copilot_internal user/quota
  CopilotDeviceFlow.cs  optional GitHub device-flow login
UI/
  IconFactory.cs        runtime-drawn tray icon
  PopupForm.cs          dark popover with bars + countdowns
```

Adding a provider = implement `IUsageProvider`, return a `ProviderSnapshot` with `UsageWindow`s, and add it
to the list in `TrayAppContext`.

---

## Known limitations / next steps

- No automatic token refresh — same fragility CodexBar documents. The Copilot device-flow token and Claude
  cookie will eventually expire and need re-auth.
- OpenAI shows spend only (the API has no subscription reset windows).
- No automatic browser-cookie import yet. On Windows that means reading the Chromium cookie DB and
  decrypting with **DPAPI** (`System.Security.Cryptography.ProtectedData`) — doable in C#, deliberately
  left out of this starter to keep it simple and avoid crossing an app boundary.
- A real settings window (instead of editing JSON) and per-provider account switching are obvious additions.
