# GramChat

A lightweight desktop and mobile client that puts Instagram on Windows, Linux and Android **without Reels and Explore**.

> Everything Instagram has — feed, profiles, follow requests, private accounts, notifications, settings, teen and parental supervision, stories, and Direct Messages — with Reels and Explore kept out of reach.

> **Not affiliated with Instagram or Meta.** GramChat is an independent, unofficial client. It embeds Instagram's own web experience; it does not reimplement, scrape or modify Instagram's service.

---

## Overview

Instagram's app has grown far beyond messaging and photos. Reels and Explore are engineered to be endlessly scrollable, and they are where most of the "I lost an hour" time goes — which is exactly why many parents, schools and workplaces block them.

**GramChat** gives you Instagram without that. It wraps Instagram's official web client and enforces a single, narrow rule at two levels: **Reels and Explore are never reachable.** Everything else is the real Instagram — the same feed, the same profiles, the same follow requests, the same DMs, the same settings, and the same parental/teen supervision controls you already use.

---

## Features

Available on every platform:

- Secure Instagram login (including two-factor and login challenges)
- Home feed, search-by-profile, profiles, follow/unfollow and **follow requests**
- **Private accounts** — the request/approve flow works exactly as on Instagram
- Notifications and activity
- Direct Messages: conversations, group chats, sending and receiving, attach
- Voice and video calls in DMs and group chats (WebRTC) with hardware-aware tuning
- Threads/stories and profile highlights
- Full account settings, including **teen accounts and parental supervision** (Instagram's own controls)
- **Reels and Explore blocked everywhere** — the rail tab, in-feed reels, a profile's Reels tab, `/reels/…` URLs and `/explore/…` URLs
- Links that leave Instagram open in your system browser, never inside the guarded view
- Clean, distraction-controlled interface

### Windows Features

- Microsoft Edge WebView2 host with a native navigation guard and an in-page SPA guard
- Call-friendly Chromium flags: calls keep running at full rate even when the window is minimised or occluded
- Automatic camera, microphone and notification permission grants so calls and incoming-call toasts work out of the box
- Window size/maximised state remembered between runs
- Encrypted session database (SQLCipher) with a DPAPI-protected key

### Android Features

- Same Reels/Explore-free experience as Windows
- Camera and microphone permissions for calls, plus Bluetooth audio routing for call headsets
- The screen is kept awake for the duration of a voice/video call
- Two-layer navigation guard (native + JavaScript SPA route guard)
- Page hardening: hides Reels/Explore links and tabs, mirrors toasts
- Desktop user agent for best Instagram web compatibility
- Material 3 design with Jetpack Compose UI
- Encrypted session database (SQLCipher) locked by a hardware-backed Android Keystore key

### Linux Features

- Native GTK4 + WebKitGTK 6.0 client, packaged as a `.deb`
- Same two guard layers as Windows (the policy and in-page script are literally the same source files)
- Voice and video call support via WebRTC (camera/mic auto-granted on call)
- Desktop notification permission auto-granted (new-message and incoming-call toasts)
- Single-instance application (a second launch focuses the existing window)
- Window size/maximised state remembered between runs
- Offline overlay with retry, keyboard shortcuts (Ctrl+H home, Ctrl+D inbox, Ctrl+N new message, F5 reload, Ctrl+Shift+L log out)
- Log out & clear local data from the Privacy menu

---

## Why This Application Exists

Many users:

- Want Instagram's genuinely useful parts — messages, photos, family and friends
- Do **not** want the Reels/Explore recommendation engine
- Are managing screen time, for themselves or for a teenager
- Already use Instagram's parental supervision and want a client that respects it rather than routing around it

GramChat keeps the parts of Instagram you asked for and quietly removes the two that are designed to be hard to put down.

---

## How It Works

GramChat authenticates with your Instagram account inside Instagram's own web client and then polices **where that client may go**:

1. Log into your Instagram account (on Instagram's own secure page).
2. Use Instagram normally — feed, profiles, messages, settings.
3. Reels and Explore are blocked before they ever render.

### The two guard layers

Neither layer reads, records or transmits your messages. They only inspect web addresses.

1. **Native navigation guard** (`Services/NavigationPolicy.cs`, mirrored in `webview/NavigationPolicy.kt`) — every top-level navigation and every popup is classified as Allow / Bounce-to-Home / Block / Open-external. Reels and Explore routes are bounced back to the Home feed; links that leave Instagram are handed to the system browser; everything else is allowed through. Reels is matched **per path segment**, so it catches the rail tab (`/reels/`), the reel viewer (`/reel/ABC/`), a profile's tab (`/someuser/reels/`) and audio pages (`/reels/audio/123/`).
2. **In-page SPA guard** (`Services/PageHardening.cs`, mirrored in `webview/PageHardening.kt`) — Instagram's web client is a React single-page app, so most in-app clicks never trigger a real navigation. An injected script hooks the History API, checks the route, and force-returns to the Home feed if it ever lands on a blocked route. It also **hides every link and tab that points at Reels or Explore** (rail icons, in-feed reels, a profile's Reels tab), mirrors Instagram's in-app toasts into a readable overlay, and relays the unread count into the window title.

The in-page guard is **event-driven**: a single `MutationObserver` collapsed into one animation frame drives all DOM work, with one slow 2-second backstop. While a voice/video call is on screen it suspends that work entirely so the renderer's main thread stays free for WebRTC, and it tells the shell to keep the screen awake (Android).

> **Notifications** come in two flavours: OS-level toasts produced by the host (granted automatically on first use) and Instagram's own in-app banners, which the app keeps readable via the toast mirror above.

---

## Security & Privacy

Your privacy is important.

### We Do Not

- Store your Instagram password
- Sell user data
- Track conversations for advertising
- Access any part of Instagram you did not open

### We Do

- Encrypt sensitive local data
- Store saved logins in an on-device encrypted database (see below)
- Use secure authentication methods
- Minimise collected information
- Respect Instagram's own privacy and supervision controls

---

### Encrypted session database (all platforms)

Each client keeps a small local SQLite database of saved logins encrypted with
**AES-256 (SQLCipher)** — the entire file is ciphertext, so nothing readable is
written to disk. What it stores is *session data only*: the Instagram session
cookies and minimal account metadata (user id, username, display name, avatar,
last login). **The Instagram password is never seen, stored, or transmitted by
GramChat** — you always sign in on Instagram's own secure page.

The database's 256-bit key never sits in plain text on disk:

| Platform | Key protection |
| --- | --- |
| Windows | DPAPI (`ProtectedData`, current-user scope — the same mechanism Chromium uses for WebView2 cookies) |
| Linux | A 256-bit key file readable only by your user account (0600) — Chromium's Linux model |
| Android | A key generated inside the hardware-backed **Android Keystore**, wrapping the database key |

The clients use the database in three ways:

1. **Snapshot** — after a successful login, the session cookies (plus the profile of the signed-in user) are captured and stored encrypted.
2. **Restore** — on startup, if the browser profile has no session, the most recent login is decrypted and re-injected, so you stay signed in even after clearing browser data.
3. **Logout** — `Log out & clear local data…` removes all stored logins from the database along with the cookies/cache/site data.

> Linux note: WebKitGTK itself writes its cookie file (`cookies.sqlite`) in the profile directory in plain SQLite — WebKitGTK has no at-rest cookie encryption, unlike Chromium on Windows. GramChat mitigates this by keeping the profile folder and key file owner-only (0700/0600), mirroring the snapshot into the encrypted database, and restoring from it. The AES-256 snapshot is the authoritative copy of your saved login.

> Users should always review the application's privacy policy before use.

---

## System Requirements

### Windows

- Windows 10 or newer
- Internet connection
- Active Instagram account
- The Microsoft Edge WebView2 runtime (pre-installed on up-to-date Windows 10/11)

### Android

- Android 7.0 (API 24) or higher
- Internet connection
- Active Instagram account
- Camera and microphone permissions (for voice/video calls)

### Linux

- 64-bit x86 Linux (Ubuntu 24.04+, Debian 13+, Fedora 41+, Arch) — needs WebKitGTK 6.0
- Internet connection
- Active Instagram account
- Camera and microphone access (for voice/video calls)

---

## Planned Features

### iOS Support

Native iPhone and iPad application with the same Reels/Explore-free scope.

### Future Enhancements

- Dark Mode
- Notification customization
- Optional PIN lock / focus schedules on top of Instagram's own supervision
- Accessibility improvements
- Enhanced security options

---

## Roadmap

### Version 1.0 (Windows)

- [x] Instagram login
- [x] Full Instagram experience (feed, profiles, follow, notifications, settings)
- [x] Direct messaging support
- [x] Voice and video calls
- [x] Reels/Explore guard (two layers)

### Version 1.0 (Android)

- [x] Instagram login
- [x] Full Instagram experience
- [x] Voice and video calls
- [x] Navigation guards (Reels/Explore)
- [x] Page hardening
- [x] Encrypted session database (Keystore)

### Version 1.0 (Linux) — `v1.0-Linux-beta1`

- [x] Instagram login
- [x] Full Instagram experience
- [x] Voice and video calls
- [x] Navigation guards (Reels/Explore)
- [x] Page hardening
- [x] `.deb` packaging (amd64)

### Version 1.5

- [ ] Improved notifications
- [ ] Multiple accounts
- [ ] Account switching UI

---

## How the Windows App Keeps Reels and Explore Out

The Windows client (`src/GramChat`) embeds Instagram's official web client inside Microsoft Edge WebView2 and enforces the blocklist at two levels:

1. **Native navigation guard** (`Services/NavigationPolicy.cs`) — every top-level navigation and every popup window is classified:
   - **Reels/Explore** → cancelled, bounced back to the Home feed;
   - **external link** (anything outside `instagram.com`) → opened in your system browser;
   - **Everything else on Instagram** → allowed.
2. **In-page SPA guard** (`Services/PageHardening.cs`) — hooks the History API and re-checks the route, force-returning to Home if the SPA ever lands on a Reels/Explore route. It also:
   - hides the Reels and Explore links/tabs wherever they appear;
   - mirrors each in-app toast into a corner overlay and holds it on screen for ~6 seconds, so "Message sent" and friends stay readable;
   - relays the unread-message count from the page title into the window title.

> **Calling.** WebView2 is launched with `--autoplay-policy=no-user-gesture-required` (so the incoming-call ringtone plays without a prior click) plus `--disable-background-timer-throttling`, `--disable-renderer-backgrounding` and `--disable-backgrounding-occluded-windows`, so a call keeps running at full rate while the window is minimised, occluded or unfocused. Camera, microphone and desktop notifications are granted automatically; all other permissions keep WebView2's standard prompt.

Privacy notes:

- Your password is never seen or stored — login happens on Instagram's own secure page.
- Session cookies live in an encrypted WebView2 profile under `%LOCALAPPDATA%\GramChat\WebView2` (Chromium encrypts them with Windows DPAPI).
- Saved logins are mirrored into an AES-256 (SQLCipher) database at `%LOCALAPPDATA%\GramChat\sessions.db` (key protected by Windows DPAPI) and restored automatically if the browser profile is ever cleared.
- `Privacy → Log out & clear local data…` deletes the cookies, caches, site storage **and** every login stored in the encrypted database.

---

## Building the Windows App from Source

### Prerequisites

- Windows 10 or newer
- [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) (any recent patch)
- The Microsoft Edge WebView2 runtime (pre-installed on up-to-date Windows 10/11)

### Build & run

```powershell
dotnet build src/GramChat/GramChat.csproj -c Release

# the executable lands here:
start src/GramChat/bin/Release/net10.0-windows/GramChat.exe
```

> In this workspace a project-local SDK lives at `../tools/dotnet-sdk` (installed with
> `tools/dotnet-install.ps1`); if the `dotnet` command is not on your PATH, invoke that
> `dotnet.exe` instead.

### Publish a distributable build

Framework-dependent (small, needs .NET Desktop Runtime on the target PC):

```powershell
dotnet publish src/GramChat/GramChat.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

Self-contained single file (**no .NET runtime, Node.js or other prerequisites on the target PC** — the runtime and all required libraries are bundled into one exe):

```powershell
dotnet publish src/GramChat/GramChat.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Output lands in `src/GramChat/bin/Release/net10.0-windows/win-x64/publish/` and is a single `GramChat.exe` (plus optional `.pdb`/`.xml` debug/doc files you can delete).

> The build is not code-signed, so Windows SmartScreen may show a warning the first time someone runs a downloaded copy — choose "More info → Run anyway".

### Developer tools inside the app

DevTools are disabled by default. Enable them with the `GRAMCHAT_DEVTOOLS=1` environment variable (or run under a debugger). Useful shortcuts: `Ctrl+H` home, `Ctrl+D` inbox, `Ctrl+N` new message, `F5` reload, `Ctrl+Shift+L` log out & clear data.

---

## How the Android App Keeps Reels and Explore Out

The Android client (`android/app`) wraps Instagram's web client in a WebView and enforces the same blocklist at two levels:

1. **Native navigation guard** (`webview/NavigationPolicy.kt`) — every URL the WebView tries to load is classified as Allow / Bounce-to-Home / Block / Open-external. Reels and Explore routes are bounced back to Home; external links open in your default browser.
2. **In-page SPA guard** (`webview/PageHardening.kt`) — hooks the History API, hides Reels/Explore links and tabs, mirrors toasts, and relays the unread count to the title bar.

Voice and video calls work through Instagram's web WebRTC implementation — the app grants camera and microphone permissions via `WebChromeClient.onPermissionRequest()`, keeps the screen awake while a call is active, and declares Bluetooth audio routing so call headsets work.

Privacy notes:

- Your password is never seen or stored — login happens on Instagram's own secure page.
- Session cookies live in the WebView's standard cookie storage.
- Saved logins are stored in an AES-256 (SQLCipher) database locked by a hardware-backed **Android Keystore** key, and restored automatically if the WebView's cookies are ever cleared.
- The app uses a desktop Chrome user agent for best Instagram web compatibility.
- `Privacy → Log out & clear local data` deletes all cookies, caches, site storage **and** every login stored in the encrypted database.

---

## Building the Android App from Source

### Prerequisites

- JDK 17 or newer
- Android SDK (API 34, build tools 34.0.0)
- Kotlin 1.9.22+

### Build

```bash
cd android
./gradlew assembleDebug
```

The APK lands at `android/app/build/outputs/apk/debug/app-debug.apk`.

Or use the build script (sets up JDK/SDK paths automatically):

```bash
cd android
./build.sh
```

The script copies the APK to the project root as `GramChat-Android-v<version>-debug.apk`.

### Install on device

```bash
adb install android/app/build/outputs/apk/debug/app-debug.apk
```

> The Android application id is `com.gramchat.app`.

---

## How the Linux App Keeps Reels and Explore Out

The Linux client (`src/GramChatLinux`) is the C# twin of the Windows client, built on GTK4 + WebKitGTK 6.0 (via the GirCore bindings) instead of WPF + WebView2. It shares the exact same two guard layers because the policy and the injected script are the same source files (`Services/NavigationPolicy.cs` and `Services/PageHardening.cs`, linked into both projects):

1. **Native navigation guard** — every navigation and every new-window request is classified as Allow / Bounce-to-Home / Block / Open-external before WebKit starts the load.
2. **In-page SPA guard** — the same script as Windows is injected at document start: it watches the History API and the pathname, force-returns the SPA to Home if it lands on Reels/Explore, and hides the Reels/Explore links and tabs.

Voice and video calls work through Instagram's WebRTC implementation: camera and microphone requests are granted automatically, and media autoplay is allowed so the incoming-call ringtone plays without a prior click.

Privacy notes:

- Your password is never seen or stored — login happens on Instagram's own secure page.
- Session cookies live in a dedicated profile under `~/.local/share/GramChat` (WebKitGTK profile directories are isolated from the system browser; the profile folder is owner-only).
- Saved logins are stored in an AES-256 (SQLCipher) database at `~/.local/share/GramChat/sessions.db` whose key file is readable only by your user account, and restored automatically if the profile is cleared.
- `Privacy → Log out & clear local data…` clears all cookies, caches, site storage **and** every login stored in the encrypted database.
- No analytics, no advertising SDKs, no data collection.

---

## Building and installing the Linux App (.deb)

### Prerequisites (build machine)

- .NET SDK 10.0 (auto-detected; the workspace-local SDK works too)
- Python 3 (only used to assemble the `.deb`; no `dpkg` required, so the package can be built from the Windows checkout as well)

### Build the .deb

```bash
./linux/build-deb.sh
```

This publishes `src/GramChatLinux` self-contained for `linux-x64` and writes `dist/GramChat-1.0-Linux-beta1-amd64.deb`.

### Install

On Debian/Ubuntu (with WebKitGTK 6.0, e.g. Ubuntu 24.04+, Debian 13+):

```bash
sudo apt install ./dist/GramChat-1.0-Linux-beta1-amd64.deb
```

The package installs the app to `/usr/lib/gramchat`, a `gramchat` launcher on the PATH, a desktop entry, and the icon. Uninstall with `sudo apt remove gramchat`.

> **Requirements at runtime:** the package depends on `libwebkitgtk-6.0-4` (WebKitGTK 6.0) and `libgtk-4-1`, which `apt` installs automatically on Debian/Ubuntu. Fedora users can install `webkitgtk6` + `gtk4` and convert the package with `alien`, or run the published binary directly from `src/GramChatLinux/bin/Release/net10.0/linux-x64/publish/`.

### Developer tools inside the app

Same as Windows: set `GRAMCHAT_DEVTOOLS=1` (or run under a debugger) to enable WebKitGTK's developer extras. Shortcuts: `Ctrl+H` home, `Ctrl+D` inbox, `Ctrl+N` new message, `F5` reload, `Ctrl+Shift+L` log out & clear data.
