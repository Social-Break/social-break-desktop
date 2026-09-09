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

## Releasing a version

### 1. Bump the version in both places

`SocialBreakTray.csproj`'s `<Version>` and `installer/SocialBreakTray.iss`'s
`MyAppVersion` must match. Nothing enforces it - a mismatch produces an installer whose
Add/Remove Programs entry disagrees with the binary it installs.

### 2. Stop any running copy

The tray app holds a lock on its own `.exe`, so a build while it's running fails with
`MSB3027: ... file is locked by: SocialBreakTray`. It runs detached and survives closing
the terminal that started it, so this catches people out:

```
taskkill /IM SocialBreakTray.exe /F
```

### 3. Publish

Target the app project explicitly. Running this at the solution root also tries to
publish `SocialBreakTray.Tests`, which fails with `NETSDK1098` because a single-file
publish requires an app host that a test project doesn't have:

```
dotnet publish SocialBreakTray/SocialBreakTray.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The Inno Setup script packages this self-contained single-file output, so end users need
no .NET runtime of their own.

### 4. Compile the installer

From the repository root - the path below is relative to it, not to `SocialBreakTray/`:

```
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\SocialBreakTray.iss
```

The result is `installer/Output/SocialBreakTraySetup.exe`. The `.iss` reads the publish
output from the exact path step 3 writes to, so publishing first is not optional.

### 5. Publish the release and repoint the website

The website's Download button redirects to a fixed release tag, so a new installer that
isn't released - or is released under a tag the site doesn't reference - changes nothing
for anyone:

```
gh release create v1.0.4 installer/Output/SocialBreakTraySetup.exe \
  --repo Social-Break/social-break-desktop --title "Social Break Desktop 1.0.4" --notes "..."
```

Then update the redirect in the website repo (`core/views.py`, search for
`releases/download/`) and push. Until that lands, the button keeps serving the previous
build.

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

## Tests

```
dotnet test
```

`SocialBreakTray.Tests` covers the two pieces of pure logic that carry the most risk:

- **`LimitEvaluator`** - the hand-port of `background.js`'s
  `isLimitReached`/`getDomainLimit`/`getLogicalDjangoDay`. Ports drift, and this repo's
  history already contains several "this rule silently never fired" fixes, so the tests
  pin down all four plan types, the Monday=0..Sunday=6 remap, the `resetHour` shift, the
  `window_is_block` inversion, and the reason-code strings shared with `block.html`.
- **`UsageAccumulator`** - the daily/weekly rollover boundaries and the
  persist-on-every-write durability, including the "relaunch after being killed must not
  lose the week" case that `report-usage`'s snapshot-overwrite semantics make dangerous.

Both classes take optional clock (and, for the accumulator, state-path) parameters that
exist solely as test seams; every production call site passes neither and behaves exactly
as before. The path seam is not optional cosmetics - without it a test run would overwrite
the live `%APPDATA%\SocialBreak\usage.dat` and destroy a real week of tracked totals.

Everything below still needs a real Windows machine and a real login:

## Manual verification checklist (run against each release)

- [ ] First launch shows the disclosure dialog once, then the login form.
- [ ] The login window opens with **both** field captions readable - WinForms hides a
      placeholder as soon as its box has focus, so focusing a field on open leaves it
      blank and unlabelled.
- [ ] **"Sign in with your browser" opens the default browser, and approving there signs
      the app in within a couple of seconds** - the route that exists because a Google
      account has no password to type and this window has no Google button. Also confirm
      "Not now" leaves the app waiting rather than signing in, and that the link (which
      reads "Cancel" while waiting) stops it.
- [ ] A wrong password shows the server's own sentence, not "Server error (401)".
- [ ] "Use a connect code" accepts a code from the website's Trackers page, pasted with
      or without its dash.
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
- [ ] **Leave the app running across the 3am rollover with time accrued the evening
      before, then confirm the previous day still appears in a custom date range** -
      the rollover clears the daily counters, and a finished day is only preserved by
      being parked in `PendingDays` first. Set `resetHour` a few minutes ahead to test
      this without waiting for 3am.
- [ ] Confirm a tracked app's time shows up when a single day is requested (the phone's
      Usage Time screen, or `/api/usage-range/?start=&end=`) - per-day reporting is what
      lets the computer appear in any range that isn't a whole week.
- [ ] Toggling "Start with Windows" adds/removes the registry value and needs no
      elevation prompt.
- [ ] "Log Out" clears the local token and returns to the login form on restart.
- [ ] Launching the `.exe` a second time while one instance is already running does
      nothing (confirms the single-instance Mutex).
