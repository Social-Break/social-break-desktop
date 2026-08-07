# Social Break Desktop (Windows Tray App)

A minimal Windows tray companion for Social Break - tracks time spent in native desktop
applications (VS Code, Discord, etc.) the same way the browser extension tracks browser
tabs, reporting to the same backend. No dashboard of its own; everything is managed on
the website (Media List, Plan, limits). See `/mnt/d/Documents/social_break_extension` for
the browser extension this mirrors, and `templates/legal.html`'s "Desktop App: What It
Can Access" section in the main Django repo for the exact privacy commitments this code
needs to keep.

**This project has not been compiled or run** - it was written in an environment with no
`dotnet` runtime or Windows desktop session available. Everything below is based on
standard, well-documented .NET 8/WinForms/Win32 patterns, but it needs a real build and
the manual checklist below before it's trustworthy.

## Building

Requires the .NET 8 SDK (with Windows desktop workloads) on Windows.

```
cd SocialBreakTray
dotnet build
dotnet run
```

Or open `SocialBreakTray.sln` in Visual Studio 2022+.

## Project layout

- `Program.cs` - entry point, single-instance Mutex check.
- `TrayApplicationContext.cs` - the entire UI: tray icon, menu, heartbeat/sync timers.
- `Auth/` - login form + DPAPI-encrypted token storage.
- `Onboarding/` - the one-time "what this app does" disclosure shown before first login.
- `Api/` - HTTP client + DTOs for the Django REST API.
- `Tracking/` - foreground-window detection, idle detection, the disk-persisted usage
  accumulator (see its docstring for why persistence isn't optional).
- `Enforcement/` - the ported limit-checking logic, and the block overlay shown when a
  limit is hit.
- `AutoStart.cs` - the (user-toggled, never silent) `HKCU\...\Run` registry entry.

## Known limitations (v1, deliberate scope cuts - see the approved plan)

- No app icon resource yet - uses `SystemIcons.Application` as a placeholder. Add a real
  `.ico` and wire it into `TrayApplicationContext`'s `NotifyIcon.Icon` and the `.csproj`'s
  commented-out `ApplicationIcon`.
- Daily reset hour is hardcoded to 3am (matching the browser extension's own default),
  not yet exposed as a setting in this app the way it is in the extension's options page.
- Store/UWP-packaged apps' foreground window is often owned by a shared host process
  (`ApplicationFrameHost.exe`) rather than the app's own exe - not handled. Both of the
  app's stated example targets (VS Code, Discord) are classic Win32 apps and unaffected.
- The extension_info.html "Download for Windows" link is a `#` placeholder until there's
  a real installer/release to point at.

## Manual verification checklist (once built on real Windows)

Business logic (`UsageAccumulator`'s week-rollover math, `LimitEvaluator`'s ported limit
logic) is isolated behind `IForegroundWindowProvider`/`IIdleTimeProvider` specifically so
it can be unit-tested with fakes via `dotnet test`, without a live desktop session -
worth adding a test project before relying on this in production. The rest needs a real
Windows machine:

- [ ] First launch shows the disclosure dialog once, then the login form.
- [ ] Successful login stores a token and starts the tray icon/menu.
- [ ] Tray tooltip and menu status update as you switch between tracked/untracked apps.
- [ ] Focusing a tracked app (added as a `desktop_app` entry on the website's Media List)
      accrues time; switching away or going idle for 60s+ stops it.
- [ ] Alt-tabbing to a different application (leaving a tracked app's window merely
      backgrounded, not closed) also stops accrual - this is the desktop equivalent of
      the "minimized browser" fix already made to the extension, so worth confirming
      explicitly.
- [ ] Hitting a configured limit shows the block overlay, minimizes the tracked app's
      window, and "Snooze 5 minutes" works.
- [ ] **Kill the process via Task Manager mid-week, relaunch, and confirm the server-side
      weekly total did not drop** - the concrete test for `UsageAccumulator`'s disk
      persistence actually working as intended.
- [ ] Toggling "Start with Windows" adds/removes the registry value and needs no
      elevation prompt.
- [ ] "Log Out" clears the local token and returns to the login form on restart.
- [ ] Launching the `.exe` a second time while one instance is already running does
      nothing (confirms the single-instance Mutex).
