// In App.xaml.cs, in metoda ConfigureServices(), adauga dupa linia cu IHotkeyManager:

// Phase 7 — Screenshot Tool
services.AddSingleton<IScreenshotService, ScreenshotService>();
services.AddSingleton<ScreenshotViewModel>();
