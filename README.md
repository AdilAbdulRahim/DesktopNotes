# DesktopNotes

![CI](https://github.com/AdilAbdulRahim/DesktopNotes/actions/workflows/ci.yml/badge.svg)

**A lightweight note-taking and drawing layer for the Windows desktop.**

I built DesktopNotes to make it easy to capture thoughts and sketch ideas right where I work. It runs as a transparent desktop overlay, and I welcome people to explore the project, share ideas, and contribute.

## What it does

- Create notes anywhere on the desktop, with quick-start bullet and numbered lists.
- Draw freehand ink directly over the desktop.
- Undo and redo edits, notes, and drawing strokes.
- Save notes and drawings locally and restore them when the app starts.
- Access the app from the system tray; it can also start with Windows.

## What I learned

Building this project gave me hands-on experience with:

- Creating a desktop UI with **C#, WPF, and XAML** on **.NET 8**.
- Connecting interactive controls, keyboard shortcuts, text editing, and ink input.
- Working with Windows desktop behavior and native APIs to layer an app over the wallpaper.
- Persisting application data locally with JSON and WPF's ink serialization.
- Improving readability by sampling wallpaper colors and adjusting text contrast.

## Build and run

Requires Windows and the .NET 8 SDK.

```powershell
dotnet build DesktopNotes.sln -c Release
```

Run the app from Visual Studio or launch the built executable from `DesktopNotes\bin\Debug\net8.0-windows\`.

Notes and drawings are stored locally in `%LOCALAPPDATA%\DesktopNotes`.

## Project details

- **Platform:** Windows
- **Technologies:** C#, WPF, XAML, .NET 8
- **License:** MIT

---

Built by [Adil Abdul Rahim](https://github.com/AdilAbdulRahim) as a hands-on project in desktop application development.
