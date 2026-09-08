# InstaChat

A lightweight desktop and mobile application that allows Instagram users with parental restrictions enabled on Instagram Reels to access **only their Instagram direct messages (DMs)** without exposing Reels, Explore, Feed recommendations, or other distracting content.

> Stay connected through Instagram chats while keeping social media distractions out of reach.

---

## Overview

Instagram's parental restrictions and content controls are designed to protect users, but many users still need access to direct messages for communication with friends, family, work contacts, or school groups.

**InstaChat** provides a chat-focused Instagram experience on both Windows and Android by allowing users to access only their Instagram messaging functionality.

The application removes unnecessary distractions and focuses exclusively on communication.

---

## Features

### Windows Features

- Secure Instagram authentication
- Access to Instagram Direct Messages
- View existing conversations
- Send and receive messages
- Voice and video calls in DMs and group chats (WebRTC)
- Real-time chat updates
- Clean and distraction-free interface
- No Instagram Reels access
- No Explore page access
- No content feed access
- No story browsing
- Lightweight desktop experience

### Android Features

- Same DM-only experience as Windows
- Voice and video call support via WebRTC
- Camera and microphone permissions for calls
- Two-layer navigation guard (native + JavaScript SPA route guard)
- Page hardening: hides non-DM navigation links, covers status bar notch, mirrors toasts
- Desktop user agent for best Instagram web compatibility
- Material 3 design with Jetpack Compose UI

---

## Why This Application Exists

Many users:

- Have parental restrictions enabled on Instagram
- Want to communicate with friends and family
- Need access to group chats
- Prefer a distraction-free messaging experience
- Don't want to spend time scrolling through Reels or Explore

InstaChat provides a focused solution by separating messaging from the rest of the Instagram platform.

---

## How It Works

The application authenticates with the user's Instagram account and provides an interface dedicated exclusively to messaging functionality.

Users can:

1. Log into their Instagram account.
2. View their conversations.
3. Open chats.
4. Send and receive messages.
5. Stay connected without access to distracting Instagram content.

---

## Security & Privacy

Your privacy is important.

### We Do Not

- Store your Instagram password
- Sell user data
- Track conversations for advertising
- Access unnecessary Instagram content

### We Do

- Encrypt sensitive local data
- Use secure authentication methods
- Minimize collected information
- Respect user privacy

> Users should always review the application's privacy policy before use.

---

## System Requirements

### Windows

- Windows 10 or newer
- Internet connection
- Active Instagram account

### Android

- Android 7.0 (API 24) or higher
- Internet connection
- Active Instagram account
- Camera and microphone permissions (for voice/video calls)

---

## Planned Features

### iOS Support

Native iPhone and iPad application with messaging-focused functionality.

### Future Enhancements

- Dark Mode
- Notification customization
- Chat search
- Message filtering
- Multiple account support
- Accessibility improvements
- Enhanced security options

---

## Roadmap

### Version 1.0 (Windows)

- [x] Instagram login
- [x] Direct messaging support
- [x] Conversation management
- [x] Voice and video calls
- [x] Clean UI

### Version 1.0 (Android)

- [x] Instagram login
- [x] Direct messaging support
- [x] Voice and video calls
- [x] Navigation guards (DM-only)
- [x] Page hardening

### Version 1.5

- [ ] Improved notifications
- [ ] Chat search
- [ ] Multiple accounts

---

## How the Windows App Locks Instagram to Chat

The Windows client (`src/InstaChat`) embeds Instagram's official web client inside
Microsoft Edge WebView2 and enforces a **Direct-Messages-only allowlist** at two levels:

1. **Native navigation guard** (`Services/DmNavigationPolicy.cs`) — every top-level navigation
   and every popup window is checked against an allowlist (DMs, login/two-factor/challenge
   pages, internal Instagram endpoints). Everything else — feed, explore, reels, stories,
   profiles, post pages — is cancelled and the app bounces back to the DM inbox.
2. **In-page SPA guard** (`Services/PageHardening.cs`) — Instagram's web client is a React
   single-page app whose in-app clicks never trigger a real navigation. An injected script
   hooks the History API and re-checks the route every 500 ms, force-returning to the inbox
   if the SPA ever leaves the chat experience. It also tidies the page:
   - hides the distracting links (Home, Explore, Reels, logo, the Create button, …) from
     the navigation rail only — in-app toasts elsewhere on the page are left alone;
   - covers Instagram's own header strip that peeks out under the app's title bar (the
     "notch" reported by testers);
   - mirrors each in-app toast into a corner overlay and holds it on screen for ~6 seconds,
     so "Message sent" and friends stay readable instead of vanishing instantly;
   - relays the unread-message count from the page title into the window title.

> **Notifications** come in two flavours: Windows-level toasts produced by WebView2 (granted
> automatically on first use) and Instagram's own in-app banners, which the app keeps
> readable via the toast mirror above. Nothing was removed from either path.

Privacy notes:

- Your password is never seen or stored — login happens on Instagram's own secure page.
- Session cookies live in an encrypted WebView2 profile under
  `%LOCALAPPDATA%\InstaChat\WebView2` (Chromium encrypts them with Windows DPAPI).
- `Privacy → Log out & clear local data…` deletes all cookies, caches and site storage.
- Camera and microphone are granted automatically so voice/video calls work (the page
  only uses them after you start or accept a call); desktop notification permission is
  granted automatically so new-message and incoming-call toasts work. All other
  permissions (geolocation, sensors, ...) keep WebView2's standard prompt.

---

## Building the Windows App from Source

### Prerequisites

- Windows 10 or newer
- [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) (any recent patch)
- The Microsoft Edge WebView2 runtime (pre-installed on up-to-date Windows 10/11)

### Build & run

```powershell
dotnet build src/InstaChat/InstaChat.csproj -c Release

# the executable lands here:
start src/InstaChat/bin/Release/net10.0-windows/InstaChat.exe
```

> In this workspace a project-local SDK lives at `../tools/dotnet-sdk` (installed with
> `tools/dotnet-install.ps1`); if the `dotnet` command is not on your PATH, invoke that
> `dotnet.exe` instead.

### Publish a distributable build

Framework-dependent (small, needs .NET Desktop Runtime on the target PC):

```powershell
dotnet publish src/InstaChat/InstaChat.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

Self-contained single file (**no .NET runtime, Node.js or other prerequisites on the target
PC** — the runtime and all required libraries are bundled into one exe):

```powershell
dotnet publish src/InstaChat/InstaChat.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

Output lands in `src/InstaChat/bin/Release/net10.0-windows/win-x64/publish/` and is
a single ~66 MB `InstaChat.exe` (plus optional `.pdb`/`.xml` debug/doc files you can
delete). Ship that one file, or a zip of it like `dist/InstaChat-1.0-beta1-win-x64.zip`.

> The build is not code-signed, so Windows SmartScreen may show a warning the first time
> someone runs a downloaded copy — choose "More info → Run anyway".

### Developer tools inside the app

DevTools are disabled by default. Enable them with the `INSTACHAT_DEVTOOLS=1` environment
variable (or run under a debugger). Useful shortcuts: `Ctrl+D` inbox, `Ctrl+N` new message,
`F5` reload, `Ctrl+Shift+L` log out & clear data.

---

## How the Android App Locks Instagram to Chat

The Android client (`android/app`) wraps Instagram's web client in a WebView and enforces the same **Direct-Messages-only allowlist** at two levels:

1. **Native navigation guard** (`webview/DmNavigationPolicy.kt`) — every URL the WebView tries to load is classified as Allow, BounceToInbox, or Block. Non-DM Instagram pages (feed, explore, reels, stories) are blocked or bounced back to the inbox.
2. **In-page SPA guard** (`webview/PageHardening.kt`) — the same JavaScript injection approach as Windows: hooks the History API, hides non-DM navigation links, covers the status bar notch, mirrors toasts, and relays the unread count to the title bar.

Voice and video calls work through Instagram's web WebRTC implementation — the app grants camera and microphone permissions via `WebChromeClient.onPermissionRequest()`.

Privacy notes:

- Your password is never seen or stored — login happens on Instagram's own secure page.
- Session cookies live in the WebView's standard cookie storage.
- The app uses a desktop Chrome user agent for best Instagram web compatibility.
- `Privacy → Log out & clear local data` deletes all cookies, caches, and site storage.

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

The script copies the APK to the project root as `InstaChat-Android-v<version>-debug.apk`.

### Install on device

```bash
adb install android/app/build/outputs/apk/debug/app-debug.apk
```

### Download pre-built APK

Pre-built debug APKs are attached to each [GitHub release](https://github.com/AayushKumar1028/Insta-chat/releases).

