# BatterMouse: Tray App & Toast Notifications Research

**Project:** BatterMouse
**Researched:** 2026-03-12
**Overall confidence:** HIGH (all claims verified against official Microsoft docs or current official sources)

---

## 1. System Tray Implementation

### Options Considered

#### Option A: WinForms `NotifyIcon` + `ApplicationContext` (RECOMMENDED)

The simplest, battle-tested approach for a no-window background app in C#. No form is required — use a custom `ApplicationContext` subclass as the app host.

```csharp
// Program.cs
[STAThread]
static void Main()
{
    ApplicationConfiguration.Initialize();
    Application.Run(new TrayApplicationContext());
}

// TrayApplicationContext.cs
public class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, OnExit);

        _trayIcon = new NotifyIcon
        {
            Icon = new Icon("app.ico"),
            Text = "BatterMouse",
            Visible = true,
            ContextMenuStrip = menu
        };
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }
}
```

**Why this over the alternatives:**
- Zero dependency beyond `System.Windows.Forms` (included in .NET 8 Windows TFM)
- `ApplicationContext` gives a message pump without any visible window
- No WPF or WinUI overhead — keeps the binary small
- Works with `OutputType=WinExe` to suppress the console window

**Project file minimum:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
</Project>
```

The Windows TFM (`net8.0-windows10.0.19041.0`) is required to access WinRT toast APIs later.

#### Option B: H.NotifyIcon (WPF/WinUI)

[H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon) (NuGet: `H.NotifyIcon`) is a modern reimplementation that supports WPF, WinUI 3, MAUI, and console apps. It has explicit "windowless" sample apps. Good choice if you want WinUI 3 or MAUI styling for a future settings flyout — but adds meaningful framework weight for BatterMouse's minimal scope.

**Skip for BatterMouse v1.** Raw `NotifyIcon` + `ApplicationContext` is sufficient and lighter.

#### Option C: Raw Win32 `Shell_NotifyIcon`

`Shell_NotifyIcon` is the underlying Win32 API that `System.Windows.Forms.NotifyIcon` wraps. Using it directly via P/Invoke gives maximum control but requires significant boilerplate (window procedure, message loop, NOTIFYICONDATA struct). No advantage over option A for this use case.

**Skip entirely.** The WinForms wrapper provides everything needed without the pain.

### Gotcha: Suppress the Taskbar Button

With `OutputType=WinExe` and no `Form`, there is no taskbar button to worry about. If you ever add a form and want it hidden from the taskbar, set `ShowInTaskbar = false` and `WindowState = FormWindowState.Minimized` before showing it.

---

## 2. Windows 11 Toast Notifications

### Modern vs. Legacy

| Approach | API | Windows 11 Look | Action Center | Verdict |
|---|---|---|---|---|
| `NotifyIcon.ShowBalloonTip()` | Win32 balloon | No — old bubble style | No | Avoid |
| WinRT `ToastNotification` | `Windows.UI.Notifications` | Yes | Yes | Use this |

Balloon tips look visually out of place on Windows 11 and do not appear in Action Center. WinRT toast notifications render with the modern notification card style, persist in Action Center, and support buttons/images.

### NuGet Package

```
Microsoft.Toolkit.Uwp.Notifications >= 7.0
```

This package (maintained by the Community Toolkit, sourced from the Windows Community Toolkit repo) wraps WinRT toast APIs with a fluent C# builder and handles the heavy lifting for unpackaged apps. The `ToastContentBuilder` API produces valid notification XML, and `ToastNotificationManagerCompat` manages AUMID registration automatically for unpackaged apps.

Install:
```
dotnet add package Microsoft.Toolkit.Uwp.Notifications
```

### AUMID: Packaged vs. Unpackaged

**AUMID** (Application User Model ID) is a string Windows uses to associate a running process with its notifications, taskbar pinning, and jump lists.

| App Type | AUMID Source | What You Must Do |
|---|---|---|
| MSIX packaged | Set automatically from `Package.appxmanifest` identity | Nothing |
| Unpackaged (plain EXE) | Must be registered in registry or auto-derived | Use `ToastNotificationManagerCompat` — it auto-registers |

For **unpackaged apps**, `ToastNotificationManagerCompat` (from the Toolkit package) performs automatic registration on first call. It writes a registry entry under `HKCU\Software\Classes\AppUserModelId\<your-aumid>` using the EXE path as the AUMID and the assembly display name as the display name. You do **not** need to write registry code manually for basic send-only scenarios.

**Critical requirement:** Use `ToastNotificationManagerCompat`, NOT `ToastNotificationManager`. The raw `ToastNotificationManager` will throw `Element not found` (0x80070490) when called from a process without package identity.

### Minimal Fire-and-Forget Toast (Unpackaged)

```csharp
// Requires: net8.0-windows10.0.19041.0 TFM (or later)
// Requires: Microsoft.Toolkit.Uwp.Notifications >= 7.0
using Microsoft.Toolkit.Uwp.Notifications;

new ToastContentBuilder()
    .AddText("BatterMouse")
    .AddText("Keychron mouse battery is at 20%. Charge soon.")
    .Show();
```

No AUMID setup code needed — `ToastNotificationManagerCompat` handles it on first `Show()`.

### Notification Activation (Click Handling)

For BatterMouse v1, the toast is purely informational — no click action needed. If you want to handle clicks in a future version:

```csharp
// Subscribe before showing any notifications (at app startup)
ToastNotificationManagerCompat.OnActivated += args =>
{
    // Called on background thread — marshal to UI thread if needed
    var parsedArgs = ToastArguments.Parse(args.Argument);
    // e.g. show a settings window
};
```

For unpackaged apps, if the app is closed when the notification is clicked, Windows re-launches the EXE. Check `ToastNotificationManagerCompat.WasCurrentProcessToastActivated()` at startup to detect this case.

### Cleanup (Uninstall / Portable App)

Since BatterMouse will likely be a portable EXE with no installer, call this on exit to clean up registry entries and scheduled/pending notifications:

```csharp
ToastNotificationManagerCompat.Uninstall();
```

### AUMID Length Limit

Keep your internal AUMID to 129 characters or fewer. Exceeding 129 characters breaks scheduled notifications (throws 0x8007007A). The auto-derived AUMID from the Toolkit uses the EXE path — on most installs this will be well under the limit, but placing the EXE deep in a long path could theoretically hit it. Not a practical concern for a normal install.

---

## 3. Auto-Start on Windows Login

### Three Options

#### Option A: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (RECOMMENDED)

The per-user registry Run key. Programs listed here launch when the current user logs in.

```csharp
using Microsoft.Win32;

private static void SetAutoStart(bool enable)
{
    using var key = Registry.CurrentUser.OpenSubKey(
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);

    if (enable)
        key!.SetValue("BatterMouse", Environment.ProcessPath!);
    else
        key!.DeleteValue("BatterMouse", throwOnMissingValue: false);
}
```

**Why this is the right choice for BatterMouse:**

| Criterion | HKCU Run | Startup Folder | Task Scheduler |
|---|---|---|---|
| Admin rights required | No | No | No (for user tasks) |
| User can disable via Task Manager Startup tab | Yes | Yes | Yes |
| Survives EXE rename/move | No — path stored | Only if shortcut updated | No — path stored |
| Complexity | 3 lines of code | File copy + shortcut creation | Full XML task definition |
| Standard for tray apps | Yes | Less common | Overkill |

**Gotcha:** The value must point to the exact EXE path. If the user moves the EXE, auto-start silently fails. Consider storing the path at registration time and providing a "re-register" option in the tray menu.

**Gotcha:** `HKLM\...\Run` (machine-wide) requires admin rights to write. Always use `HKCU` unless you have an installer with elevation.

#### Option B: Startup Folder

Drop a `.lnk` shortcut in `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup`. Functionally equivalent to HKCU Run. Slightly more visible to end users browsing that folder. Requires creating a shell shortcut programmatically (needs the `Windows Script Host` COM object or a library), which is more code than the registry approach.

Skip for BatterMouse — HKCU Run is simpler.

#### Option C: Task Scheduler

Create a task triggered at user logon via `TaskScheduler` API or XML. Supports delay, conditions (network available, etc.), and running elevated without a UAC prompt. Complete overkill for a tray app. Requires either admin rights to register or careful use of user-scope task scheduler.

Skip for BatterMouse.

### Exposing the Option

Offer "Launch at startup" as a checkable item in the tray context menu. Toggle the registry key on check/uncheck. Read the key on startup to set the initial check state.

---

## 4. App Packaging: MSIX vs. Plain EXE

### Recommendation: Plain EXE (unpackaged)

| Concern | MSIX | Plain EXE |
|---|---|---|
| Toast notifications | Full support, AUMID automatic | Full support via `ToastNotificationManagerCompat` |
| HID device access | No restriction (standard HID uses inbox drivers) | No restriction |
| Deployment | Requires MSIX bundle or Store | xcopy / single file |
| Auto-start registry write | Must use `windows.startup` extension in manifest | Direct registry write |
| Uninstall cleanup | Automatic | Manual (`Uninstall()` call) |
| Code signing requirement | Yes (for side-load without Store) | Optional but recommended |
| Complexity | Significant (manifest, packaging project, cert) | Minimal |

**HID access is identical between MSIX and plain EXE** — standard HID devices use Windows inbox drivers (`HidUsb.sys`, `HidClass.sys`), which are accessible from both. MSIX restrictions only apply to apps requiring a custom kernel driver. A Keychron mouse dongle presenting as a standard HID device has no access difference.

**The main MSIX advantage** — automatic AUMID and cleaner toast activation — is fully compensated by `Microsoft.Toolkit.Uwp.Notifications` for unpackaged apps.

**Decision:** Ship as a plain self-contained EXE. Revisit MSIX only if Microsoft Store distribution becomes a requirement.

---

## 5. Single-File EXE Deployment

### Feasibility: YES, with caveats

.NET 8 supports `PublishSingleFile=true` for `win-x64` (and `win-x86`, `win-arm64`). For a WinForms app using WinRT toast APIs this is the recommended distribution format for a portable utility.

### Project file configuration

```xml
<PropertyGroup>
  <PublishSingleFile>true</PublishSingleFile>
  <SelfContained>true</SelfContained>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
</PropertyGroup>
```

Publish command:
```
dotnet publish -c Release -r win-x64
```

Output: a single `.exe` in `publish/` — no installer needed.

### Key Caveats

**Native DLLs are not bundled by default.** The .NET runtime's native libraries (`.dll` files in the runtime) are placed as sibling files unless you also set:

```xml
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
```

With this flag, native libs are extracted to `%TEMP%\.net\BatterMouse\` on first run and reused on subsequent runs. This is transparent to the user but means first launch touches disk in the temp folder.

**Without `IncludeNativeLibrariesForSelfExtract`:** The output is still a single managed EXE but there will be a handful of native `.dll` files alongside it. For a truly zero-friction portable app, include the flag.

**`Assembly.Location` returns empty string** inside a single-file bundle. Do not use it to find files next to the EXE. Use `AppContext.BaseDirectory` or `Environment.ProcessPath` instead.

**File size:** Self-contained .NET 8 WinForms with WinRT bindings will be approximately 60–90 MB before trimming, 20–35 MB after trimming (trim results vary with reflection usage; WinForms + WinRT may not trim cleanly — test before relying on it).

**Trimming warning:** `Microsoft.Toolkit.Uwp.Notifications` uses reflection internally. Aggressive trimming may break toast functionality. Test trimmed output before shipping. If trimming causes issues, ship without it — 80 MB self-contained is acceptable for a desktop utility.

### API Incompatibilities in Single-File Mode

Avoid these APIs (they break or return wrong values inside a single-file bundle):

| API | Behavior |
|---|---|
| `Assembly.Location` | Returns empty string |
| `Assembly.CodeBase` | Throws `PlatformNotSupportedException` |
| `Assembly.GetFile()` | Throws `IOException` |
| `Module.FullyQualifiedName` | Returns `<Unknown>` |

Use `Environment.ProcessPath` to get the EXE path (needed for the auto-start registry value).

---

## 6. Summary Recommendations for BatterMouse

| Topic | Decision | Rationale |
|---|---|---|
| Tray implementation | WinForms `NotifyIcon` + `ApplicationContext` | Simplest, no extra deps, perfect for no-window background app |
| Toast notifications | `Microsoft.Toolkit.Uwp.Notifications` >= 7.0 | Handles AUMID auto-registration for unpackaged apps, fluent API |
| AUMID registration | Automatic via `ToastNotificationManagerCompat` | No manual registry code required for send-only scenario |
| Auto-start | `HKCU\...\Run` registry key | No admin rights, 3 lines of code, toggleable from tray menu |
| Packaging | Plain EXE (unpackaged) | No MSIX complexity, no impact on HID or toast capability |
| Deployment | `PublishSingleFile=true` + `SelfContained=true` | Single portable EXE, no installer needed |

### Critical Gotchas Ordered by Impact

1. **Use `ToastNotificationManagerCompat` not `ToastNotificationManager`.** The raw manager throws on unpackaged apps.
2. **TFM must be `net8.0-windows10.0.19041.0`** (or later Windows build). Without a Windows TFM, `Show()` is missing and WinRT types are inaccessible.
3. **Use `HKCU` Run key, not `HKLM`.** HKLM requires admin elevation to write.
4. **Use `Environment.ProcessPath` for the auto-start registry value** (not `Assembly.Location` — broken in single-file bundles).
5. **Call `ToastNotificationManagerCompat.Uninstall()` on app exit** for a portable app to clean up Action Center and registry entries.
6. **Test single-file trimming separately** — toast notification libraries use reflection and may break under aggressive trimming.

---

## Sources

- [Microsoft Learn — Send local toast from C# app (official, updated 2025-07-29)](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/send-local-toast)
- [Microsoft Learn — Send local toast from other unpackaged apps (official, updated 2026-02-12)](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/send-local-toast-other-apps)
- [Microsoft Learn — WinForms NotifyIcon component](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/app-icons-to-the-taskbar-with-wf-notifyicon)
- [Microsoft Learn — .NET single file deployment overview (updated 2025-10-22)](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [Microsoft Learn — Run and RunOnce registry keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)
- [H.NotifyIcon GitHub — tray icon library for WPF/WinUI/MAUI](https://github.com/HavenDV/H.NotifyIcon)
- [NuGet — Microsoft.Toolkit.Uwp.Notifications 7.1.3](https://www.nuget.org/packages/Microsoft.Toolkit.Uwp.Notifications/)
- [Albert Akhmetov — Creating a Context Menu for Tray Icons in C# and WinUI (2025)](https://albertakhmetov.com/posts/2025/creating-a-context-menu-for-tray-icons-in-c%23-and-winui/)
