// ── MainWindow.xaml.cs ──────────────────────────────────────────────────────
// 1. Adauga parametru in constructor:
//    ScreenshotViewModel screenshotVm
//
// 2. In constructor, dupa DataContext = _vm;
//    ScreenshotViewControl.DataContext = screenshotVm;
//
// 3. In OnHotkeyPressed, adauga cazul pentru Ctrl+Shift+S:
//    case "Screenshot":
//        _ = screenshotVm.TriggerFullScreenAsync();
//        break;
//
// ── MainWindow.xaml ─────────────────────────────────────────────────────────
// Inlocuieste TabItem-ul cu TextBlock "Screenshot Tool — Phase 7" cu:
//
//  <TabItem Header="📷 Screenshot">
//      <views:ScreenshotView x:Name="ScreenshotViewControl"/>
//  </TabItem>
//
// ── MainWindow.xaml.cs — Register hotkey Ctrl+Shift+S ──────────────────────
// In OnLoaded, dupa hotkey-urile existente:
//
//  _hotkeyManager.Register(new HotkeyConfig
//  {
//      Id = 3,
//      Action = "Screenshot",
//      Modifiers = "Ctrl+Shift",
//      Key = "S",
//      IsEnabled = true
//  });
