# Social Break Desktop (Windows Tray App)

A minimal Windows tray companion for Social Break - tracks time spent in native desktop
applications (VS Code, Discord, etc.) the same way the browser extension tracks browser
tabs, reporting to the same backend. No dashboard of its own; everything is managed on
the website (Media List, Plan, limits). See the `social-break-extension` repo for the
browser extension this mirrors, and `templates/legal.html`'s "Desktop App: What It Can
Access" section in the main Django repo for the exact privacy commitments this code needs
to keep.

Originally written in an environment with no `dotnet` runtime or Windows desktop session
available, so every line of it was unverified. It has since been built, installed and run
on real Windows, and is published as a release (see Releases) that the website's "Download
for Windows" link points at. The checklist at the bottom is kept as a per-release
regression pass, not as a list of things never yet tried.

## Building

Targets `net8.0-windows`. A newer SDK is fine - the .NET 10 SDK builds this target
without the .NET 8 SDK installed, since it restores the `net8.0` targeting packs on
demand.

```
cd SocialBreakTray
dotnet build
dotnet run
```

Or open `SocialBreakTray.sln` in Visual Studio 2022+.

Note that `dotnet run` / a plain `dotnet build` produces a framework-dependent binary,
which needs the .NET 8 **Desktop** Runtime present to launch - a machine with only a
newer runtime installed will refuse to start it with "You must install or update .NET to
run this application". Publishing self-contained (below) sidesteps that entirely and is
what the installer ships.

## Publishing an installer

The Inno Setup script packages the self-contained single-file publish output, so end
users need no .NET runtime of their own. Build in this order:

```
cd SocialBreakTray
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Then compile `installer/SocialBreakTray.iss` - either open it in the Inno Setup Compiler
GUI and hit Build > Compile, or use its command-line compiler:

```
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\SocialBreakTray.iss
```

The result is `installer/Output/SocialBreakTraySetup.exe`. The `.iss` reads the publish
output from the exact path above, so publishing first is not optional.

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

- Daily reset hour is hardcoded to 3am (matching the browser extension's own default),
  not yet exposed as a setting in this app the way it is in the extension's options page.
- Store/UWP-packaged apps' foreground window is often owned by a shared host process
  (`ApplicationFrameHost.exe`) rather than the app's own exe - not handled. Both of the
  app's stated example targets (VS Code, Discord) are classic Win32 apps and unaffected.
- The website's "Download for Windows" button (`extension_info.html`) routes through the
  Django repo's `download_windows` view, which redirects to a **hardcoded release tag**.
  Cutting a new release here does not update it: bump the tag in that view and deploy the
  website too, or the button keeps serving the previous build.

## Manual verification checklist (run against each release)

Business logic (`UsageAccumulator`'s week-rollover math, `LimitEvaluator`'s ported limit
logic) is isolated behind `IForegroundWindowProvider`/`IIdleTimeProvider` specifically so
it can be unit-tested with fakes via `dotnet test`, without a live desktop session -
**there is still no test project**, so every item below is currently checked by hand.
Adding one would let the first two categories be covered automatically; the rest need a
real Windows machine either way:

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
