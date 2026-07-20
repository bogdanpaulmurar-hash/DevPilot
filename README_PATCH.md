# DevPilot — Fix ZIP v1 (Teme + Jiggler + Heartbeat AutoRestart + Screenshot removed)

## Fișiere incluse

| Fișier | Destinație în proiect | Acțiune |
|--------|----------------------|---------|
| `DarkTheme.xaml`          | `DevPilot.UI\Styles\DarkTheme.xaml`                    | **Înlocuiește** |
| `LightTheme.xaml`         | `DevPilot.UI\Styles\LightTheme.xaml`                   | **Înlocuiește** |
| `JigglerViewModel.cs`     | `DevPilot.UI\ViewModels\JigglerViewModel.cs`           | **Înlocuiește** |
| `HeartbeatService.cs`     | `DevPilot.Infrastructure\Services\HeartbeatService.cs` | **Înlocuiește** |
| `HeartbeatViewModel.cs`   | `DevPilot.UI\ViewModels\HeartbeatViewModel.cs`         | **Înlocuiește** |
| `HeartbeatView.xaml`      | `DevPilot.UI\Views\HeartbeatView.xaml`                 | **Înlocuiește** |
| `MainViewModel.cs`        | `DevPilot.UI\ViewModels\MainViewModel.cs`              | **Înlocuiește** |
| `AppSettings_PATCH.txt`   | instrucțiuni pentru `AppSettings.cs`                     | **Citește** |

---

## Pași de aplicare

### 1. Teme (DarkTheme + LightTheme)
Copiază fișierele în `DevPilot.UI\Styles\` și înlocuiește cele existente.
Rebuild → temele vor folosi paleta Siemens corectă (#1E1E2E dark, #F3F3F0 light, #009999 teal).

### 2. JigglerViewModel
Înlocuiește fișierul. Adaugă în `JigglerView.xaml` dacă lipsesc:
```xml
<TextBlock Text="{Binding IntervalLabel}" .../>
<Slider IsEnabled="{Binding IsNotRunning}" .../>
```

### 3. AppSettings.cs — MANUAL (1 linie)
Deschide `DevPilot.Models\AppSettings.cs` și adaugă în secțiunea Heartbeat:
```csharp
[JsonPropertyName("heartbeat_auto_restart")]
public bool HeartbeatAutoRestart { get; set; } = false;
```

### 4. HeartbeatService + HeartbeatViewModel + HeartbeatView
Înlocuiește toate 3 fișierele.
HeartbeatViewModel acum injectează `HeartbeatService` (concret, nu interfață)
pentru a accesa proprietatea `AutoRestart`.

**IMPORTANT — App.xaml.cs DI:** Dacă ai înregistrat `IHeartbeatService`,
schimbă înregistrarea să expună și concretul:
```csharp
// Înlocuiește:
services.AddSingleton<IHeartbeatService, HeartbeatService>();
// Cu:
services.AddSingleton<HeartbeatService>();
services.AddSingleton<IHeartbeatService>(sp => sp.GetRequiredService<HeartbeatService>());
```
Și în înregistrarea HeartbeatViewModel:
```csharp
services.AddSingleton<HeartbeatViewModel>();
```

### 5. MainViewModel — Screenshot eliminat
ScreenshotViewModel a fost scos. Dacă în App.xaml.cs mai ai:
```csharp
services.AddSingleton<ScreenshotViewModel>();
```
Poți lăsa înregistrarea (nu strică), dar șterge tab-ul Screenshot din MainWindow.xaml.

### 6. MainWindow.xaml — Screenshot tab
Șterge sau comentează tab-ul Screenshot din navigarea principală.

---

## Ce s-a schimbat

### Teme
- Dark: `#1E1E2E` background, `#252535` surface, `#2A2A3E` card, `#009999` accent
- Light: `#F3F3F0` background, `#FFFFFF` card, `#009999` accent
- Toate stilurile: PrimaryButton, SecondaryButton, DangerButton, InputStyle,
  ComboStyle, CardStyle, NavButtonStyle, NavButtonActiveStyle, SliderStyle,
  CheckStyle, GridStyle, SeparatorStyle, StatusBarStyle

### JigglerViewModel
- `IntervalLabel` → `"Interval: 60s"` (pentru TextBlock binding)
- `IsNotRunning` → `!IsRunning` (pentru Slider IsEnabled binding)

### HeartbeatService
- `AutoRestart` property (bool, default false)
- `TryRestart()` → relansează procesul mort cu `Process.Start()`
- Tracking procese gestionate în `_managed` dictionary
- Log identic cu Master Orchestrator: `"AutoRestart: 'TruePortal' relaunched (PID=1234)"`

### HeartbeatViewModel
- `AutoRestart` property cu binding la CheckBox
- Persistat în `AppSettings.HeartbeatAutoRestart`
- StatusText arată `| 🔄 AutoRestart ON` când e activ

### MainViewModel
- `ScreenshotViewModel` eliminat din constructor și proprietăți
