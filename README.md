# DevPilot — IT Automation Hub v1.0

**Autor:** Bogdan Murar (Z003FU5C) — Siemens AG  
**Language:** C# WPF .NET 8  
**Primary Color:** #009999 (Siemens Teal)

---

## Structura Solution

```
DevPilot/
├── DevPilot.sln
├── DevPilot.UI/                    ← WPF entry point (WinExe)
│   ├── App.xaml / App.xaml.cs      ← DI container setup
│   ├── AppConstants.cs
│   ├── Views/
│   │   └── MainWindow.xaml/.cs
│   ├── ViewModels/
│   │   └── MainViewModel.cs
│   ├── Commands/
│   │   └── RelayCommand.cs
│   └── Styles/
│       ├── DarkTheme.xaml
│       └── LightTheme.xaml
├── DevPilot.Core/                  ← Interfaces only
│   └── Interfaces/
│       ├── ISettingsManager.cs
│       ├── IJigglerService.cs
│       ├── IHeartbeatService.cs
│       ├── IHotkeyManager.cs
│       ├── IScreenshotService.cs
│       └── ILauncherService.cs
├── DevPilot.Models/                ← Pure data models
│   ├── AppEntry.cs
│   ├── HotkeyConfig.cs
│   ├── HeartbeatTarget.cs
│   └── AppSettings.cs
├── DevPilot.Infrastructure/        ← Service implementations
│   └── Services/
│       └── SettingsManager.cs
└── DevPilot.Tests/                 ← xUnit tests
    └── Services/
        └── SettingsManagerTests.cs
```

## Phase Log

| Phase | Status      | Description                          |
|-------|-------------|--------------------------------------|
| 1     | ✅ DELIVERED | Solution Skeleton + DI + Settings    |
| 2     | ⏳ Pending   | App Launcher Panel                   |
| 3     | ⏳ Pending   | External Tools Launchers             |
| 4     | ⏳ Pending   | System Tray + Single Instance        |
| 5     | ⏳ Pending   | Mouse Jiggler                        |
| 6     | ⏳ Pending   | Global Hotkeys                       |
| 7     | ⏳ Pending   | Screenshot Tool                      |
| 8     | ⏳ Pending   | Heartbeat Monitor                    |
| 9     | ⏳ Pending   | Settings Tab (full)                  |
| 10    | ⏳ Pending   | Polish + Tests + Release             |

## Build & Run

```bash
cd DevPilot
dotnet build DevPilot.sln
dotnet run --project DevPilot.UI
```

## Run Tests

```bash
dotnet test DevPilot.Tests
```
