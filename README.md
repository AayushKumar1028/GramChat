# GramChat

GramChat is an unofficial Instagram client for **Windows**, **Android**, and **Linux** (iOS planned). It gives you the full Instagram web experience — feed, profiles, follow requests, private accounts, notifications, settings, teen accounts and parental supervision, and Direct Messages with voice/video calls — while keeping **Reels and Explore** out of reach.

> Not affiliated with Instagram or Meta. GramChat embeds Instagram's own web client; it does not reimplement, scrape, or modify Instagram's service.

## What it does

- Secure Instagram sign-in on Instagram's own pages
- Home feed, profiles, follow / follow requests, private accounts
- Notifications, settings, teen accounts and parental supervision (Instagram's own controls)
- Direct Messages with voice and video calls (WebRTC)
- Reels and Explore hard-blocked by a native + in-page guard
- External links open in your system browser

## Platforms

- **Windows**: WPF + WebView2 (`src/GramChat`)
- **Android**: WebView + Jetpack Compose (`android/app`, id `com.gramchat.app`)
- **Linux**: GTK4 + WebKitGTK 6.0 (`src/GramChatLinux`)
- **iOS**: planned

## Privacy

- Login happens on Instagram's own pages
- Passwords are never seen or stored
- Session data is kept in an encrypted local profile and can be cleared from the in-app Privacy menu

## Build from source

### Windows
```powershell
dotnet build src/GramChat/GramChat.csproj -c Release
start src/GramChat/bin/Release/net10.0-windows/GramChat.exe
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
Package output: `dist/GramChat-1.0-Linux-beta1-amd64.deb`

## Releases

Prebuilt binaries/APKs are available on the [Releases page](https://github.com/AayushKumar1028/GramChat/releases).
