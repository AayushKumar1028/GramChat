# InstaChat

InstaChat is a DM-only Instagram client for **Windows**, **Android**, and **Linux**.  
It lets you use Instagram Direct Messages (including voice/video calls) without access to Reels, Explore, or the main feed.

## What it does

- Secure Instagram sign-in
- View, send, and receive DMs
- Voice/video calls in chats and groups (WebRTC)
- Blocks non-chat Instagram navigation with native + in-page guards
- Distraction-free UI focused on messaging

## Platforms

- **Windows**: WPF + WebView2 (`src/InstaChat`)
- **Android**: WebView + Jetpack Compose (`android/app`)
- **Linux**: GTK4 + WebKitGTK (`src/InstaChatLinux`)

## Privacy

- Login happens on Instagram's own pages
- Passwords are not stored by InstaChat
- Local site/session data can be cleared from the in-app Privacy menu

## Build from source

### Windows
```powershell
dotnet build src/InstaChat/InstaChat.csproj -c Release
start src/InstaChat/bin/Release/net10.0-windows/InstaChat.exe
```

### Android
```bash
cd android
./gradlew assembleDebug
```
APK output: `android/app/build/outputs/apk/debug/app-debug.apk`

### Linux (.deb)
```bash
./linux/build-deb.sh
```
Package output: `dist/InstaChat-1.0-Linux-beta1-amd64.deb`

## Releases

Prebuilt binaries/APKs are available on the [Releases page](https://github.com/AayushKumar1028/Insta-chat/releases).
