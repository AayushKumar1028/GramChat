# InstaChat Access

A lightweight desktop application that allows Instagram users with parental restrictions enabled on Instagram Reels to access **only their Instagram direct messages (DMs)** without exposing Reels, Explore, Feed recommendations, or other distracting content.

> Stay connected through Instagram chats while keeping social media distractions out of reach.

---

## Overview

Instagram's parental restrictions and content controls are designed to protect users, but many users still need access to direct messages for communication with friends, family, work contacts, or school groups.

**InstaChat Access** is a Windows application that provides a chat-focused Instagram experience by allowing users to access only their Instagram messaging functionality.

The application removes unnecessary distractions and focuses exclusively on communication.

---

## Features

### Current Features (Windows)

- Secure Instagram authentication
- Access to Instagram Direct Messages
- View existing conversations
- Send and receive messages
- Real-time chat updates
- Clean and distraction-free interface
- No Instagram Reels access
- No Explore page access
- No content feed access
- No story browsing
- Lightweight desktop experience

---

## Why This Application Exists

Many users:

- Have parental restrictions enabled on Instagram
- Want to communicate with friends and family
- Need access to group chats
- Prefer a distraction-free messaging experience
- Don't want to spend time scrolling through Reels or Explore

InstaChat Access provides a focused solution by separating messaging from the rest of the Instagram platform.

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

---

## Planned Features

### Android Support

Native Android application allowing the same chat-only Instagram experience.

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

### Version 1.0

- [x] Instagram login
- [x] Direct messaging support
- [x] Conversation management
- [x] Clean UI

### Version 1.5

- [ ] Improved notifications
- [ ] Chat search
- [ ] Multiple accounts

---

## How the Windows App Locks Instagram to Chat

The Windows client (`src/InstaChatAccess`) embeds Instagram's official web client inside
Microsoft Edge WebView2 and enforces a **Direct-Messages-only allowlist** at two levels:

1. **Native navigation guard** (`Services/DmNavigationPolicy.cs`) — every top-level navigation
   and every popup window is checked against an allowlist (DMs, login/two-factor/challenge
   pages, internal Instagram endpoints). Everything else — feed, explore, reels, stories,
   profiles, post pages — is cancelled and the app bounces back to the DM inbox.
2. **In-page SPA guard** (`Services/PageHardening.cs`) — Instagram's web client is a React
   single-page app whose in-app clicks never trigger a real navigation. An injected script
   hooks the History API and re-checks the route every 500 ms, force-returning to the inbox
   if the SPA ever leaves the chat experience. It also hides the distracting links
   (Home, Explore, Reels, logo, …) from the navigation rail and relays the unread-message
   count from the page title into the window title.

Privacy notes:

- Your password is never seen or stored — login happens on Instagram's own secure page.
- Session cookies live in an encrypted WebView2 profile under
  `%LOCALAPPDATA%\InstaChatAccess\WebView2` (Chromium encrypts them with Windows DPAPI).
- `Privacy → Log out & clear local data…` deletes all cookies, caches and site storage.
- Camera/microphone/geolocation prompts are left to WebView2's standard permission UI;
  desktop notification permission is granted automatically so new-message toasts work.

---

## Building the Windows App from Source

### Prerequisites

- Windows 10 or newer
- [.NET SDK 10.0](https://dotnet.microsoft.com/download/dotnet/10.0) (any recent patch)
- The Microsoft Edge WebView2 runtime (pre-installed on up-to-date Windows 10/11)

### Build & run

```powershell
dotnet build src/InstaChatAccess/InstaChatAccess.csproj -c Release

# the executable lands here:
start src/InstaChatAccess/bin/Release/net10.0-windows/InstaChatAccess.exe
```

> In this workspace a project-local SDK lives at `../tools/dotnet-sdk` (installed with
> `tools/dotnet-install.ps1`); if the `dotnet` command is not on your PATH, invoke that
> `dotnet.exe` instead.

### Publish a distributable build

Framework-dependent (small, needs .NET Desktop Runtime on the target PC):

```powershell
dotnet publish src/InstaChatAccess/InstaChatAccess.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

Self-contained (no runtime needed on the target PC, ~150 MB):

```powershell
dotnet publish src/InstaChatAccess/InstaChatAccess.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

Output lands in `src/InstaChatAccess/bin/Release/net10.0-windows/win-x64/publish/`.

### Developer tools inside the app

DevTools are disabled by default. Enable them with the `INSTACHAT_DEVTOOLS=1` environment
variable (or run under a debugger). Useful shortcuts: `Ctrl+D` inbox, `Ctrl+N` new message,
`F5` reload, `Ctrl+Shift+L` log out & clear data.

