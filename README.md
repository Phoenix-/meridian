# Meridian

A native Windows desktop calendar and task manager powered by Google Calendar and Google Tasks.

Built with WinUI 3 (.NET 10, Windows App SDK), runs unpackaged on Windows 10/11.

## Features

- Day and week views with timed event grid
- Google Tasks alongside calendar events
- Multi-account Google sign-in
- All-day events and task chips in the week view

## Requirements

- Windows 10 1903 (build 18362) or later
- Visual Studio 2022 with Windows App SDK workload

## Setup

1. Create a Google Cloud project and enable the Calendar and Tasks APIs
2. Download OAuth 2.0 client credentials as `client_secret.json` and place it next to `Calendar.sln`
3. Open `Calendar.sln` in Visual Studio 2022
4. Build and run

On first launch you will be prompted to sign in with your Google account. Tokens are stored in `%APPDATA%\Meridian\tokens\`.

## Build

```
dotnet build -r win-x64
```
