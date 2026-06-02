# FileTransfer.App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the WPF + MVVM desktop application (`FileTransfer.App`) that binds the Core library's `Node` and `PairingService` to a single-window UI implementing the user experience from the 2026-05-27 design (chat, drag-drop files, clipboard image paste, pairing-code dialog, settings).

**Architecture:** Single `net8.0-windows` WinExe project using CommunityToolkit.Mvvm 8.x source generators. ViewModels expose state and commands; Core events are marshalled to the UI thread via an `IDispatcher` abstraction (production = WPF Dispatcher; tests = synchronous). One main window switches between Unpaired and Paired DataTemplates based on `MainViewModel.State`; two modal dialogs (pairing code, settings). Composition root in `App.xaml.cs` (no DI container). xUnit tests cover ViewModels; XAML is verified by manual smoke.

**Tech Stack:** .NET 8 (`net8.0-windows`), C#, WPF, CommunityToolkit.Mvvm 8.x, xUnit. Reuses `FileTransfer.Core` for all networking, crypto, persistence.

---

## File Structure

```
src/FileTransfer.App/
  FileTransfer.App.csproj                            WinExe, UseWPF, net8.0-windows
  App.xaml / App.xaml.cs                             Composition root: load AppConfig, build services, choose MainViewModel mode, show MainWindow
  MainWindow.xaml / .cs                              DataContext = MainViewModel; ContentControl bound to State chooses UnpairedView/PairedView
  Views/
    UnpairedView.xaml                                UserControl: device list + "配对" buttons
    PairedView.xaml                                  UserControl: messages list + input box + send/pick/settings buttons
    PairingCodeDialog.xaml / .cs                     Modal Window: shows 4-digit code, [取消] / [确认]
    SettingsDialog.xaml / .cs                        Modal Window: device name, receive dir, auto-start, unpair, own fingerprint
  ViewModels/
    MainViewModel.cs                                 Top-level state machine + commands
    DeviceCandidateViewModel.cs                      One row in the discovery list
    TextMessageViewModel.cs                          One text bubble
    FileMessageViewModel.cs                          One file/image bubble (progress, state, cancel, open-folder)
    PairingCodeDialogViewModel.cs                    Pairing code dialog
    SettingsViewModel.cs                             Settings dialog
  Services/
    IDispatcher.cs / WpfDispatcher.cs                marshal to UI thread
    IFilePicker.cs / WpfFilePicker.cs                OpenFileDialog wrapper
    IFolderPicker.cs / WpfFolderPicker.cs            FolderBrowserDialog wrapper
    IClipboard.cs / WpfClipboard.cs                  clipboard image → temp PNG file
    IAutoStartRegistry.cs / WpfAutoStartRegistry.cs  HKCU\...\Run read/write
  Converters/
    FileSizeConverter.cs                             1234567 → "1.2 MB"
    TimestampConverter.cs                            DateTime → "14:32"
    BoolToVisibilityConverter.cs                     true → Visible

tests/FileTransfer.App.Tests/
  FileTransfer.App.Tests.csproj                      net8.0-windows
  Fakes/
    ImmediateDispatcher.cs                           IDispatcher: Invoke runs synchronously
    FakeFilePicker.cs                                IFilePicker: returns preset paths
    FakeFolderPicker.cs                              IFolderPicker: returns preset path
    FakeClipboard.cs                                 IClipboard: returns preset bitmap or null
    FakeAutoStartRegistry.cs                         IAutoStartRegistry: in-memory bool
  ViewModels/
    MainViewModelTests.cs
    FileMessageViewModelTests.cs
    PairingCodeDialogViewModelTests.cs
    SettingsViewModelTests.cs
```

**Boundary rationale:** Services are narrow interfaces so ViewModels never touch `Application.Current`, `OpenFileDialog`, `Clipboard.GetImage`, or the registry directly — every ViewModel test runs against in-memory fakes. ViewModels stay small (each owns one display concept). MainViewModel is the only orchestrator; sub-VMs receive a snapshot of state and commands but don't reach back to Core directly.

---

## Task 1: Scaffold the WPF project and tests

**Files:**
- Create: `src/FileTransfer.App/FileTransfer.App.csproj`
- Create: `src/FileTransfer.App/App.xaml`, `src/FileTransfer.App/App.xaml.cs`
- Create: `src/FileTransfer.App/MainWindow.xaml`, `src/FileTransfer.App/MainWindow.xaml.cs`
- Create: `tests/FileTransfer.App.Tests/FileTransfer.App.Tests.csproj`
- Create: `tests/FileTransfer.App.Tests/SmokeTest.cs`
- Modify: `FileTransfer.sln`

- [ ] **Step 1: Create the WPF project via `dotnet new`**

Run from repo root (`d:\Project\File Transfer`):

```powershell
dotnet new wpf -n FileTransfer.App -o src/FileTransfer.App -f net8.0-windows
dotnet new xunit -n FileTransfer.App.Tests -o tests/FileTransfer.App.Tests -f net8.0-windows
Remove-Item tests/FileTransfer.App.Tests/UnitTest1.cs
dotnet sln add src/FileTransfer.App/FileTransfer.App.csproj
dotnet sln add tests/FileTransfer.App.Tests/FileTransfer.App.Tests.csproj
dotnet add src/FileTransfer.App/FileTransfer.App.csproj reference src/FileTransfer.Core/FileTransfer.Core.csproj
dotnet add src/FileTransfer.App/FileTransfer.App.csproj package CommunityToolkit.Mvvm --version 8.2.2
dotnet add tests/FileTransfer.App.Tests/FileTransfer.App.Tests.csproj reference src/FileTransfer.App/FileTransfer.App.csproj
dotnet add tests/FileTransfer.App.Tests/FileTransfer.App.Tests.csproj reference src/FileTransfer.Core/FileTransfer.Core.csproj
```

- [ ] **Step 2: Set nullable + langversion in App csproj**

Edit `src/FileTransfer.App/FileTransfer.App.csproj` so the `<PropertyGroup>` contains:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net8.0-windows</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <UseWPF>true</UseWPF>
  <LangVersion>latest</LangVersion>
  <RootNamespace>FileTransfer.App</RootNamespace>
  <AssemblyName>FileTransfer.App</AssemblyName>
</PropertyGroup>
```

Edit `tests/FileTransfer.App.Tests/FileTransfer.App.Tests.csproj` PropertyGroup:

```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <IsPackable>false</IsPackable>
  <RootNamespace>FileTransfer.App.Tests</RootNamespace>
</PropertyGroup>
```

- [ ] **Step 3: Replace App.xaml.cs and MainWindow contents with the minimal placeholder**

Replace `src/FileTransfer.App/App.xaml` content with:

```xml
<Application x:Class="FileTransfer.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources/>
</Application>
```

Replace `src/FileTransfer.App/App.xaml.cs`:

```csharp
namespace FileTransfer.App;

public partial class App : System.Windows.Application
{
}
```

Replace `src/FileTransfer.App/MainWindow.xaml`:

```xml
<Window x:Class="FileTransfer.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="File Transfer" Height="600" Width="450">
    <Grid>
        <TextBlock Text="Scaffold OK" HorizontalAlignment="Center" VerticalAlignment="Center"/>
    </Grid>
</Window>
```

Replace `src/FileTransfer.App/MainWindow.xaml.cs`:

```csharp
namespace FileTransfer.App;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 4: Add a smoke test**

Create `tests/FileTransfer.App.Tests/SmokeTest.cs`:

```csharp
namespace FileTransfer.App.Tests;

public class SmokeTest
{
    [Fact]
    public void Solution_Builds_And_Tests_Run()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 5: Build and run all tests**

Run: `dotnet build` then `dotnet test`
Expected: build succeeds, all existing 65 Core tests still pass plus 1 new SmokeTest = 66 tests passing.

- [ ] **Step 6: Commit**

```powershell
git add .
git commit -m "chore(app): scaffold FileTransfer.App WPF project and test project"
```

---

## Task 2: IDispatcher abstraction (WPF + Immediate)

**Files:**
- Create: `src/FileTransfer.App/Services/IDispatcher.cs`
- Create: `src/FileTransfer.App/Services/WpfDispatcher.cs`
- Create: `tests/FileTransfer.App.Tests/Fakes/ImmediateDispatcher.cs`

- [ ] **Step 1: Write the interface and WPF implementation**

Create `src/FileTransfer.App/Services/IDispatcher.cs`:

```csharp
namespace FileTransfer.App.Services;

/// Marshals a callback to the UI thread. Production uses the WPF Dispatcher.
/// Tests inject an ImmediateDispatcher that runs callbacks synchronously on
/// the calling thread, so ObservableCollection mutations in event handlers
/// can be asserted without any dispatcher pumping.
public interface IDispatcher
{
    /// Runs `action` on the UI thread, blocking until it returns. If already on
    /// the UI thread, runs inline.
    void Invoke(Action action);

    /// Runs the async `work` on the UI thread. Returns a task that completes when
    /// the work does.
    Task InvokeAsync(Func<Task> work);
}
```

Create `src/FileTransfer.App/Services/WpfDispatcher.cs`:

```csharp
using System.Windows.Threading;

namespace FileTransfer.App.Services;

public sealed class WpfDispatcher : IDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Invoke(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.Invoke(action);
    }

    public Task InvokeAsync(Func<Task> work)
    {
        if (_dispatcher.CheckAccess()) return work();
        return _dispatcher.InvokeAsync(work).Task.Unwrap();
    }
}
```

- [ ] **Step 2: Write the failing test for the fake**

Create `tests/FileTransfer.App.Tests/Fakes/ImmediateDispatcher.cs`:

```csharp
using FileTransfer.App.Services;

namespace FileTransfer.App.Tests.Fakes;

/// IDispatcher test double: runs callbacks synchronously on the calling thread.
public sealed class ImmediateDispatcher : IDispatcher
{
    public void Invoke(Action action) => action();
    public Task InvokeAsync(Func<Task> work) => work();
}
```

Create `tests/FileTransfer.App.Tests/Services/DispatcherTests.cs`:

```csharp
using FileTransfer.App.Services;
using FileTransfer.App.Tests.Fakes;

namespace FileTransfer.App.Tests.Services;

public class DispatcherTests
{
    [Fact]
    public void ImmediateDispatcher_Invoke_RunsActionSynchronously()
    {
        IDispatcher d = new ImmediateDispatcher();
        bool ran = false;
        d.Invoke(() => ran = true);
        Assert.True(ran);
    }

    [Fact]
    public async Task ImmediateDispatcher_InvokeAsync_AwaitsWorkInline()
    {
        IDispatcher d = new ImmediateDispatcher();
        int value = 0;
        await d.InvokeAsync(async () => { await Task.Yield(); value = 42; });
        Assert.Equal(42, value);
    }
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~DispatcherTests"`
Expected: PASS (2 tests).

- [ ] **Step 4: Commit**

```powershell
git add .
git commit -m "feat(app): add IDispatcher abstraction with WPF and immediate implementations"
```

---

## Task 3: IFilePicker / IFolderPicker / IClipboard

**Files:**
- Create: `src/FileTransfer.App/Services/IFilePicker.cs`, `WpfFilePicker.cs`
- Create: `src/FileTransfer.App/Services/IFolderPicker.cs`, `WpfFolderPicker.cs`
- Create: `src/FileTransfer.App/Services/IClipboard.cs`, `WpfClipboard.cs`
- Create: `tests/FileTransfer.App.Tests/Fakes/FakeFilePicker.cs`, `FakeFolderPicker.cs`, `FakeClipboard.cs`

- [ ] **Step 1: Add the file/folder picker interfaces and fakes**

Create `src/FileTransfer.App/Services/IFilePicker.cs`:

```csharp
namespace FileTransfer.App.Services;

public interface IFilePicker
{
    /// Returns the absolute paths the user chose, or an empty array if they cancelled.
    /// Multiple selection is allowed (matches the "drop multiple" UX).
    Task<IReadOnlyList<string>> PickAsync();
}
```

Create `src/FileTransfer.App/Services/WpfFilePicker.cs`:

```csharp
using Microsoft.Win32;

namespace FileTransfer.App.Services;

public sealed class WpfFilePicker : IFilePicker
{
    public Task<IReadOnlyList<string>> PickAsync()
    {
        var dlg = new OpenFileDialog { Multiselect = true };
        bool? ok = dlg.ShowDialog();
        IReadOnlyList<string> result = ok == true ? dlg.FileNames : Array.Empty<string>();
        return Task.FromResult(result);
    }
}
```

Create `src/FileTransfer.App/Services/IFolderPicker.cs`:

```csharp
namespace FileTransfer.App.Services;

public interface IFolderPicker
{
    /// Returns the chosen directory, or null if cancelled.
    Task<string?> PickAsync(string? initialDirectory = null);
}
```

Create `src/FileTransfer.App/Services/WpfFolderPicker.cs`:

```csharp
using Microsoft.Win32;

namespace FileTransfer.App.Services;

public sealed class WpfFolderPicker : IFolderPicker
{
    public Task<string?> PickAsync(string? initialDirectory = null)
    {
        var dlg = new OpenFolderDialog();
        if (initialDirectory is not null && Directory.Exists(initialDirectory))
            dlg.InitialDirectory = initialDirectory;
        bool? ok = dlg.ShowDialog();
        return Task.FromResult(ok == true ? dlg.FolderName : null);
    }
}
```

Create `tests/FileTransfer.App.Tests/Fakes/FakeFilePicker.cs`:

```csharp
using FileTransfer.App.Services;

namespace FileTransfer.App.Tests.Fakes;

public sealed class FakeFilePicker : IFilePicker
{
    public IReadOnlyList<string> NextResult { get; set; } = Array.Empty<string>();
    public int CallCount { get; private set; }

    public Task<IReadOnlyList<string>> PickAsync()
    {
        CallCount++;
        return Task.FromResult(NextResult);
    }
}
```

Create `tests/FileTransfer.App.Tests/Fakes/FakeFolderPicker.cs`:

```csharp
using FileTransfer.App.Services;

namespace FileTransfer.App.Tests.Fakes;

public sealed class FakeFolderPicker : IFolderPicker
{
    public string? NextResult { get; set; }
    public string? LastInitialDirectory { get; private set; }

    public Task<string?> PickAsync(string? initialDirectory = null)
    {
        LastInitialDirectory = initialDirectory;
        return Task.FromResult(NextResult);
    }
}
```

- [ ] **Step 2: Add the clipboard interface and fake**

Create `src/FileTransfer.App/Services/IClipboard.cs`:

```csharp
namespace FileTransfer.App.Services;

public interface IClipboard
{
    /// If the clipboard contains an image, saves it as a PNG to a temp file and
    /// returns the absolute path. Returns null if no image is available.
    /// The caller owns the file and may move/delete it (file-transfer pipeline
    /// will move it into the receive directory eventually).
    string? GrabImageAsPng();
}
```

Create `src/FileTransfer.App/Services/WpfClipboard.cs`:

```csharp
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FileTransfer.App.Services;

public sealed class WpfClipboard : IClipboard
{
    public string? GrabImageAsPng()
    {
        if (!Clipboard.ContainsImage()) return null;
        BitmapSource? src = Clipboard.GetImage();
        if (src is null) return null;

        string path = Path.Combine(
            Path.GetTempPath(),
            $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(src));
        using var stream = File.OpenWrite(path);
        encoder.Save(stream);
        return path;
    }
}
```

Create `tests/FileTransfer.App.Tests/Fakes/FakeClipboard.cs`:

```csharp
using FileTransfer.App.Services;

namespace FileTransfer.App.Tests.Fakes;

public sealed class FakeClipboard : IClipboard
{
    /// Set to the path the next GrabImageAsPng() should return, or null for "no image".
    public string? NextResult { get; set; }
    public int CallCount { get; private set; }

    public string? GrabImageAsPng()
    {
        CallCount++;
        return NextResult;
    }
}
```

- [ ] **Step 3: Quick smoke test on the fakes**

Append to `tests/FileTransfer.App.Tests/Services/DispatcherTests.cs`:

Actually create a separate file `tests/FileTransfer.App.Tests/Services/PickerFakesTests.cs`:

```csharp
using FileTransfer.App.Services;
using FileTransfer.App.Tests.Fakes;

namespace FileTransfer.App.Tests.Services;

public class PickerFakesTests
{
    [Fact]
    public async Task FakeFilePicker_ReturnsConfiguredResult_AndCountsCalls()
    {
        var picker = new FakeFilePicker { NextResult = new[] { @"C:\a.txt", @"C:\b.txt" } };
        IReadOnlyList<string> picked = await picker.PickAsync();
        Assert.Equal(2, picked.Count);
        Assert.Equal(1, picker.CallCount);
    }

    [Fact]
    public async Task FakeFolderPicker_ReturnsConfiguredResult_AndCapturesInitial()
    {
        var picker = new FakeFolderPicker { NextResult = @"C:\Selected" };
        string? folder = await picker.PickAsync(initialDirectory: @"C:\Initial");
        Assert.Equal(@"C:\Selected", folder);
        Assert.Equal(@"C:\Initial", picker.LastInitialDirectory);
    }

    [Fact]
    public void FakeClipboard_ReturnsConfiguredResult_AndCountsCalls()
    {
        var cb = new FakeClipboard { NextResult = @"C:\img.png" };
        Assert.Equal(@"C:\img.png", cb.GrabImageAsPng());
        Assert.Equal(1, cb.CallCount);
    }
}
```

- [ ] **Step 4: Build and run**

Run: `dotnet test --filter "FullyQualifiedName~PickerFakesTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(app): add file/folder/clipboard picker services with fakes"
```

---

## Task 4: IAutoStartRegistry

**Files:**
- Create: `src/FileTransfer.App/Services/IAutoStartRegistry.cs`, `WpfAutoStartRegistry.cs`
- Create: `tests/FileTransfer.App.Tests/Fakes/FakeAutoStartRegistry.cs`

- [ ] **Step 1: Write the interface, WPF impl, and fake**

Create `src/FileTransfer.App/Services/IAutoStartRegistry.cs`:

```csharp
namespace FileTransfer.App.Services;

/// Read/write the per-user "run at logon" registry entry.
/// Hides Microsoft.Win32.Registry behind a narrow interface so ViewModel tests
/// don't need to touch HKCU.
public interface IAutoStartRegistry
{
    bool IsEnabled();
    void Enable(string executablePath);
    void Disable();
}
```

Create `src/FileTransfer.App/Services/WpfAutoStartRegistry.cs`:

```csharp
using Microsoft.Win32;

namespace FileTransfer.App.Services;

public sealed class WpfAutoStartRegistry : IAutoStartRegistry
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FileTransfer";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    public void Enable(string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true)
            ?? throw new InvalidOperationException("Cannot open HKCU Run key.");
        key.SetValue(ValueName, $"\"{executablePath}\"");
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
```

Create `tests/FileTransfer.App.Tests/Fakes/FakeAutoStartRegistry.cs`:

```csharp
using FileTransfer.App.Services;

namespace FileTransfer.App.Tests.Fakes;

public sealed class FakeAutoStartRegistry : IAutoStartRegistry
{
    public bool Enabled { get; set; }
    public string? EnabledPath { get; private set; }

    public bool IsEnabled() => Enabled;
    public void Enable(string executablePath) { Enabled = true; EnabledPath = executablePath; }
    public void Disable() { Enabled = false; EnabledPath = null; }
}
```

- [ ] **Step 2: Test the fake**

Create `tests/FileTransfer.App.Tests/Services/AutoStartRegistryFakeTests.cs`:

```csharp
using FileTransfer.App.Services;
using FileTransfer.App.Tests.Fakes;

namespace FileTransfer.App.Tests.Services;

public class AutoStartRegistryFakeTests
{
    [Fact]
    public void Enable_SetsBothFlagAndPath()
    {
        var r = new FakeAutoStartRegistry();
        Assert.False(r.IsEnabled());
        r.Enable(@"C:\app.exe");
        Assert.True(r.IsEnabled());
        Assert.Equal(@"C:\app.exe", r.EnabledPath);
    }

    [Fact]
    public void Disable_ClearsBothFlagAndPath()
    {
        var r = new FakeAutoStartRegistry { Enabled = true };
        r.Enable(@"C:\app.exe");
        r.Disable();
        Assert.False(r.IsEnabled());
        Assert.Null(r.EnabledPath);
    }
}
```

- [ ] **Step 3: Run**

Run: `dotnet test --filter "FullyQualifiedName~AutoStartRegistryFakeTests"`
Expected: PASS (2 tests).

- [ ] **Step 4: Commit**

```powershell
git add .
git commit -m "feat(app): add auto-start registry service with fake"
```

---

## Task 5: AppState enum + MainViewModel skeleton + boot routing

**Files:**
- Create: `src/FileTransfer.App/ViewModels/AppState.cs`
- Create: `src/FileTransfer.App/ViewModels/MainViewModel.cs`
- Create: `tests/FileTransfer.App.Tests/ViewModels/MainViewModelTests.cs`

This task adds the top-level state machine but only the empty shell that responds to construction (Unpaired vs Online routing). Later tasks add the events and commands.

- [ ] **Step 1: Write the failing tests**

Create `tests/FileTransfer.App.Tests/ViewModels/MainViewModelTests.cs`:

```csharp
using FileTransfer.App.Services;
using FileTransfer.App.Tests.Fakes;
using FileTransfer.App.ViewModels;

namespace FileTransfer.App.Tests.ViewModels;

public class MainViewModelTests
{
    private static MainViewModel NewVm(bool paired)
    {
        var dispatcher = new ImmediateDispatcher();
        return new MainViewModel(dispatcher, isPairedOnBoot: paired);
    }

    [Fact]
    public void State_WhenUnpaired_StartsAsUnpaired()
    {
        var vm = NewVm(paired: false);
        Assert.Equal(AppState.Unpaired, vm.State);
    }

    [Fact]
    public void State_WhenPaired_StartsAsOffline()
    {
        // When AppConfig already has a fingerprint, we boot into the paired-but-not-yet-
        // connected state. The Node will fire StatusChanged(Online) once it accepts/dials.
        var vm = NewVm(paired: true);
        Assert.Equal(AppState.Offline, vm.State);
    }
}
```

- [ ] **Step 2: Verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: FAIL — `AppState` and `MainViewModel` don't exist.

- [ ] **Step 3: Implement the enum and skeleton**

Create `src/FileTransfer.App/ViewModels/AppState.cs`:

```csharp
namespace FileTransfer.App.ViewModels;

public enum AppState
{
    Unpaired,    // Not paired yet — show device discovery + pairing
    Pairing,     // Pairing code dialog up, waiting for both sides to confirm
    Offline,     // Paired but peer not connected
    Online,      // Paired and connected
}
```

Create `src/FileTransfer.App/ViewModels/MainViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using FileTransfer.App.Services;

namespace FileTransfer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDispatcher _dispatcher;

    [ObservableProperty]
    private AppState _state;

    public MainViewModel(IDispatcher dispatcher, bool isPairedOnBoot)
    {
        _dispatcher = dispatcher;
        _state = isPairedOnBoot ? AppState.Offline : AppState.Unpaired;
    }
}
```

- [ ] **Step 4: Run**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(app): add AppState enum and MainViewModel boot-routing skeleton"
```

---

## Task 6: DeviceCandidateViewModel + Unpaired discovery

**Files:**
- Create: `src/FileTransfer.App/ViewModels/DeviceCandidateViewModel.cs`
- Modify: `src/FileTransfer.App/ViewModels/MainViewModel.cs`
- Modify: `tests/FileTransfer.App.Tests/ViewModels/MainViewModelTests.cs`

This task hooks `PairingService.PeerDiscovered` to populate a `Devices` collection and wires `RequestPairingCommand` to call `pairingService.RequestPairingAsync`. To keep tests possible without spinning real services, MainViewModel takes an abstraction over the parts of PairingService/Node it uses.

- [ ] **Step 1: Define the abstraction**

Create `src/FileTransfer.App/ViewModels/IPairingHost.cs`:

```csharp
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.ViewModels;

/// Narrow surface MainViewModel needs from PairingService — exists so tests can inject
/// a fake without spinning a real LAN socket. WpfPairingHost (added in a later App-only
/// integration task) wraps a real PairingService instance.
public interface IPairingHost
{
    event Action<PeerCandidate>? PeerDiscovered;
    event Action<string /*code*/, PeerCandidate>? PairingCandidateReady;
    event Action<PairingResult>? PairingCompleted;
    event Action<PairingFailureReason, string>? PairingFailed;

    Task StartAsync();
    Task RequestPairingAsync(PeerCandidate peer);
    Task ConfirmAsync();
    Task RejectAsync(string reason = "");
}
```

(`WpfPairingHost` and the wiring inside `App.xaml.cs` come in Task 14. Production calls forward to `PairingService` 1-to-1.)

- [ ] **Step 2: Write the failing tests**

Replace contents of `tests/FileTransfer.App.Tests/ViewModels/MainViewModelTests.cs`:

```csharp
using System.Net;
using FileTransfer.App.Services;
using FileTransfer.App.Tests.Fakes;
using FileTransfer.App.ViewModels;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.Tests.ViewModels;

public class MainViewModelTests
{
    private static (MainViewModel vm, FakePairingHost host, ImmediateDispatcher dispatcher) NewVmUnpaired()
    {
        var dispatcher = new ImmediateDispatcher();
        var host = new FakePairingHost();
        var vm = new MainViewModel(dispatcher, host, isPairedOnBoot: false);
        return (vm, host, dispatcher);
    }

    [Fact]
    public void State_WhenUnpaired_StartsAsUnpaired()
    {
        var (vm, _, _) = NewVmUnpaired();
        Assert.Equal(AppState.Unpaired, vm.State);
    }

    [Fact]
    public async Task PeerDiscovered_AddsDeviceCandidateToList()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        var peer = new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC");
        host.RaisePeerDiscovered(peer);
        Assert.Single(vm.Devices);
        Assert.Equal("Lab-PC", vm.Devices[0].DeviceName);
        Assert.Equal("DEAD", vm.Devices[0].Fingerprint);
    }

    [Fact]
    public async Task PeerDiscovered_TwiceForSameFingerprint_DoesNotDuplicate()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        var peer = new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC");
        host.RaisePeerDiscovered(peer);
        host.RaisePeerDiscovered(peer);
        Assert.Single(vm.Devices);
    }

    [Fact]
    public async Task RequestPairingCommand_ForwardsToHost()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        var peer = new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC");
        host.RaisePeerDiscovered(peer);
        var candidate = vm.Devices[0];
        await vm.RequestPairingCommand.ExecuteAsync(candidate);
        Assert.Equal("DEAD", host.LastRequestedPeer?.Fingerprint);
    }
}
```

Create `tests/FileTransfer.App.Tests/Fakes/FakePairingHost.cs`:

```csharp
using FileTransfer.App.ViewModels;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.Tests.Fakes;

public sealed class FakePairingHost : IPairingHost
{
    public event Action<PeerCandidate>? PeerDiscovered;
    public event Action<string, PeerCandidate>? PairingCandidateReady;
    public event Action<PairingResult>? PairingCompleted;
    public event Action<PairingFailureReason, string>? PairingFailed;

    public bool Started { get; private set; }
    public PeerCandidate? LastRequestedPeer { get; private set; }
    public int ConfirmCount { get; private set; }
    public int RejectCount { get; private set; }
    public string LastRejectReason { get; private set; } = "";

    public Task StartAsync() { Started = true; return Task.CompletedTask; }

    public Task RequestPairingAsync(PeerCandidate peer)
    { LastRequestedPeer = peer; return Task.CompletedTask; }

    public Task ConfirmAsync() { ConfirmCount++; return Task.CompletedTask; }

    public Task RejectAsync(string reason = "")
    { RejectCount++; LastRejectReason = reason; return Task.CompletedTask; }

    public void RaisePeerDiscovered(PeerCandidate p) => PeerDiscovered?.Invoke(p);
    public void RaisePairingCandidateReady(string code, PeerCandidate p)
        => PairingCandidateReady?.Invoke(code, p);
    public void RaisePairingCompleted(PairingResult r) => PairingCompleted?.Invoke(r);
    public void RaisePairingFailed(PairingFailureReason r, string msg)
        => PairingFailed?.Invoke(r, msg);
}
```

- [ ] **Step 3: Verify failing**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: FAIL — `Devices`, `RequestPairingCommand`, `IPairingHost`, `StartAsync` not implemented; constructor signature changed.

- [ ] **Step 4: Implement**

Create `src/FileTransfer.App/ViewModels/DeviceCandidateViewModel.cs`:

```csharp
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.ViewModels;

public sealed class DeviceCandidateViewModel
{
    public PeerCandidate Peer { get; }
    public string DeviceName => Peer.DeviceName;
    public string Fingerprint => Peer.Fingerprint;
    public DeviceCandidateViewModel(PeerCandidate peer) => Peer = peer;
}
```

Replace `src/FileTransfer.App/ViewModels/MainViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTransfer.App.Services;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDispatcher _dispatcher;
    private readonly IPairingHost _pairing;

    [ObservableProperty]
    private AppState _state;

    public ObservableCollection<DeviceCandidateViewModel> Devices { get; } = new();

    public MainViewModel(IDispatcher dispatcher, IPairingHost pairing, bool isPairedOnBoot)
    {
        _dispatcher = dispatcher;
        _pairing = pairing;
        _state = isPairedOnBoot ? AppState.Offline : AppState.Unpaired;

        _pairing.PeerDiscovered += peer => _dispatcher.Invoke(() => OnPeerDiscovered(peer));
    }

    public Task StartAsync() => _pairing.StartAsync();

    private void OnPeerDiscovered(PeerCandidate peer)
    {
        foreach (var d in Devices)
            if (d.Fingerprint == peer.Fingerprint) return;
        Devices.Add(new DeviceCandidateViewModel(peer));
    }

    [RelayCommand]
    private Task RequestPairing(DeviceCandidateViewModel? candidate)
        => candidate is null ? Task.CompletedTask : _pairing.RequestPairingAsync(candidate.Peer);
}
```

- [ ] **Step 5: Run**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```powershell
git add .
git commit -m "feat(app): wire peer discovery and RequestPairing command to MainViewModel"
```

---

## Task 7: PairingCodeDialogViewModel

**Files:**
- Create: `src/FileTransfer.App/ViewModels/PairingCodeDialogViewModel.cs`
- Create: `tests/FileTransfer.App.Tests/ViewModels/PairingCodeDialogViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/FileTransfer.App.Tests/ViewModels/PairingCodeDialogViewModelTests.cs`:

```csharp
using FileTransfer.App.ViewModels;

namespace FileTransfer.App.Tests.ViewModels;

public class PairingCodeDialogViewModelTests
{
    [Fact]
    public void Constructor_ExposesCodeAndPeerName()
    {
        var vm = new PairingCodeDialogViewModel("4837", "Desktop-XYZ");
        Assert.Equal("4837", vm.Code);
        Assert.Equal("Desktop-XYZ", vm.PeerName);
        Assert.Null(vm.Decision);
    }

    [Fact]
    public void ConfirmCommand_SetsDecisionConfirmed()
    {
        var vm = new PairingCodeDialogViewModel("4837", "Desktop-XYZ");
        vm.ConfirmCommand.Execute(null);
        Assert.Equal(PairingDecision.Confirmed, vm.Decision);
    }

    [Fact]
    public void RejectCommand_SetsDecisionRejected()
    {
        var vm = new PairingCodeDialogViewModel("4837", "Desktop-XYZ");
        vm.RejectCommand.Execute(null);
        Assert.Equal(PairingDecision.Rejected, vm.Decision);
    }
}
```

- [ ] **Step 2: Verify failing**

Run: `dotnet test --filter "FullyQualifiedName~PairingCodeDialogViewModelTests"`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement**

Create `src/FileTransfer.App/ViewModels/PairingCodeDialogViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FileTransfer.App.ViewModels;

public enum PairingDecision { Confirmed, Rejected }

public sealed partial class PairingCodeDialogViewModel : ObservableObject
{
    public string Code { get; }
    public string PeerName { get; }

    [ObservableProperty]
    private PairingDecision? _decision;

    public PairingCodeDialogViewModel(string code, string peerName)
    {
        Code = code;
        PeerName = peerName;
    }

    [RelayCommand]
    private void Confirm() => Decision = PairingDecision.Confirmed;

    [RelayCommand]
    private void Reject() => Decision = PairingDecision.Rejected;
}
```

- [ ] **Step 4: Run**

Run: `dotnet test --filter "FullyQualifiedName~PairingCodeDialogViewModelTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(app): add PairingCodeDialogViewModel with confirm/reject decision"
```

---

## Task 8: PairingCompleted/Failed handling in MainViewModel

**Files:**
- Modify: `src/FileTransfer.App/ViewModels/MainViewModel.cs`
- Modify: `tests/FileTransfer.App.Tests/ViewModels/MainViewModelTests.cs`

This task hooks the pairing flow into MainViewModel: surface `PairingCandidateReady` so the View can pop a dialog, then respond to `PairingCompleted` (persist + switch to Online/Offline) and `PairingFailed` (back to Unpaired with error message).

For decoupling from XAML, MainViewModel exposes an event `PairingCodeRequested` and a method `ConfirmPairingAsync(PairingDecision)` for the View to call.

- [ ] **Step 1: Add the failing tests**

Append to `tests/FileTransfer.App.Tests/ViewModels/MainViewModelTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task PairingCandidateReady_RaisesPairingCodeRequested()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        string? receivedCode = null;
        string? receivedPeer = null;
        vm.PairingCodeRequested += (code, peerName) =>
            { receivedCode = code; receivedPeer = peerName; };
        var peer = new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC");
        host.RaisePairingCandidateReady("4837", peer);
        Assert.Equal("4837", receivedCode);
        Assert.Equal("Lab-PC", receivedPeer);
        Assert.Equal(AppState.Pairing, vm.State);
    }

    [Fact]
    public async Task ConfirmPairing_Confirmed_ForwardsToHost()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        host.RaisePairingCandidateReady("4837", new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC"));
        await vm.RespondToPairingAsync(PairingDecision.Confirmed);
        Assert.Equal(1, host.ConfirmCount);
    }

    [Fact]
    public async Task ConfirmPairing_Rejected_ForwardsToHost()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        host.RaisePairingCandidateReady("4837", new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC"));
        await vm.RespondToPairingAsync(PairingDecision.Rejected);
        Assert.Equal(1, host.RejectCount);
    }

    [Fact]
    public async Task PairingCompleted_PersistsAndSwitchesState()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        bool persistedFired = false;
        string? persistedFingerprint = null;
        vm.PairingPersisted += result =>
            { persistedFired = true; persistedFingerprint = result.PeerFingerprint; };
        host.RaisePairingCompleted(new PairingResult("BEEF", "Lab-PC"));
        Assert.True(persistedFired);
        Assert.Equal("BEEF", persistedFingerprint);
        // After persisting, the state moves to Offline (Node not yet connected).
        Assert.Equal(AppState.Offline, vm.State);
    }

    [Fact]
    public async Task PairingFailed_GoesBackToUnpaired_WithError()
    {
        var (vm, host, _) = NewVmUnpaired();
        await vm.StartAsync();
        host.RaisePairingCandidateReady("4837", new PeerCandidate(IPAddress.Loopback, 47101, "DEAD", "Lab-PC"));
        Assert.Equal(AppState.Pairing, vm.State);
        host.RaisePairingFailed(PairingFailureReason.PeerRejected, "");
        Assert.Equal(AppState.Unpaired, vm.State);
        Assert.NotNull(vm.LastError);
        Assert.Contains("PeerRejected", vm.LastError);
    }
```

- [ ] **Step 2: Verify failing**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: FAIL — `PairingCodeRequested`, `RespondToPairingAsync`, `PairingPersisted`, `LastError` not implemented.

- [ ] **Step 3: Extend MainViewModel**

Replace `src/FileTransfer.App/ViewModels/MainViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTransfer.App.Services;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDispatcher _dispatcher;
    private readonly IPairingHost _pairing;

    [ObservableProperty]
    private AppState _state;

    [ObservableProperty]
    private string? _lastError;

    public ObservableCollection<DeviceCandidateViewModel> Devices { get; } = new();

    /// Raised when the peer's HELLO has been exchanged and the user should be shown
    /// the 4-digit code. The View handler pops PairingCodeDialog and calls
    /// RespondToPairingAsync with the user's decision.
    public event Action<string /*code*/, string /*peerName*/>? PairingCodeRequested;

    /// Raised after PairingCompleted has been observed and AppConfig persistence
    /// should happen (Composition Root performs the actual write + Node startup).
    public event Action<PairingResult>? PairingPersisted;

    public MainViewModel(IDispatcher dispatcher, IPairingHost pairing, bool isPairedOnBoot)
    {
        _dispatcher = dispatcher;
        _pairing = pairing;
        _state = isPairedOnBoot ? AppState.Offline : AppState.Unpaired;

        _pairing.PeerDiscovered += peer =>
            _dispatcher.Invoke(() => OnPeerDiscovered(peer));
        _pairing.PairingCandidateReady += (code, peer) =>
            _dispatcher.Invoke(() => OnPairingCandidate(code, peer));
        _pairing.PairingCompleted += result =>
            _dispatcher.Invoke(() => OnPairingCompleted(result));
        _pairing.PairingFailed += (reason, detail) =>
            _dispatcher.Invoke(() => OnPairingFailed(reason, detail));
    }

    public Task StartAsync() => _pairing.StartAsync();

    public Task RespondToPairingAsync(PairingDecision decision) =>
        decision == PairingDecision.Confirmed ? _pairing.ConfirmAsync() : _pairing.RejectAsync();

    private void OnPeerDiscovered(PeerCandidate peer)
    {
        foreach (var d in Devices)
            if (d.Fingerprint == peer.Fingerprint) return;
        Devices.Add(new DeviceCandidateViewModel(peer));
    }

    private void OnPairingCandidate(string code, PeerCandidate peer)
    {
        State = AppState.Pairing;
        PairingCodeRequested?.Invoke(code, peer.DeviceName);
    }

    private void OnPairingCompleted(PairingResult result)
    {
        State = AppState.Offline; // Node connection comes up via StatusChanged later
        Devices.Clear();
        LastError = null;
        PairingPersisted?.Invoke(result);
    }

    private void OnPairingFailed(PairingFailureReason reason, string detail)
    {
        State = AppState.Unpaired;
        LastError = string.IsNullOrEmpty(detail)
            ? reason.ToString()
            : $"{reason}: {detail}";
    }

    [RelayCommand]
    private Task RequestPairing(DeviceCandidateViewModel? candidate)
        => candidate is null ? Task.CompletedTask : _pairing.RequestPairingAsync(candidate.Peer);
}
```

- [ ] **Step 4: Run**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: PASS (9 tests now).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(app): wire pairing-code dialog + completion/failure events to MainViewModel"
```

---

## Task 9: TextMessageViewModel + send/receive text

**Files:**
- Create: `src/FileTransfer.App/ViewModels/TextMessageViewModel.cs`
- Create: `src/FileTransfer.App/ViewModels/INodeHost.cs`
- Modify: `src/FileTransfer.App/ViewModels/MainViewModel.cs`
- Modify: `tests/FileTransfer.App.Tests/ViewModels/MainViewModelTests.cs`
- Create: `tests/FileTransfer.App.Tests/Fakes/FakeNodeHost.cs`

This task adds text messaging. INodeHost is the narrow surface MainViewModel needs from Node; production wraps a real `Node`.

- [ ] **Step 1: Add INodeHost and FakeNodeHost**

Create `src/FileTransfer.App/ViewModels/INodeHost.cs`:

```csharp
using FileTransfer.Core;
using FileTransfer.Core.Protocol;

namespace FileTransfer.App.ViewModels;

/// Narrow surface MainViewModel uses from the Node — testable via FakeNodeHost.
public interface INodeHost
{
    ConnectionStatus Status { get; }
    string PeerName { get; }

    event Action<ConnectionStatus>? StatusChanged;
    event Action<string>? TextReceived;
    event Action<FileOffer>? FileOfferReceived;
    event Action<Guid /*id*/, long /*received*/, long /*total*/>? FileProgress;
    event Action<Guid /*id*/, string /*finalPath*/>? FileCompleted;
    event Action<Guid /*id*/, string /*reason*/>? TransferFailed;

    Task StartAsync();
    Task SendTextAsync(string text);
    Task<Guid> SendFileAsync(string path);
    Task CancelTransferAsync(Guid id);
    void Stop();
}
```

Create `tests/FileTransfer.App.Tests/Fakes/FakeNodeHost.cs`:

```csharp
using FileTransfer.App.ViewModels;
using FileTransfer.Core;
using FileTransfer.Core.Protocol;

namespace FileTransfer.App.Tests.Fakes;

public sealed class FakeNodeHost : INodeHost
{
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Offline;
    public string PeerName { get; set; } = "Peer";

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<string>? TextReceived;
    public event Action<FileOffer>? FileOfferReceived;
    public event Action<Guid, long, long>? FileProgress;
    public event Action<Guid, string>? FileCompleted;
    public event Action<Guid, string>? TransferFailed;

    public bool Started { get; private set; }
    public bool Stopped { get; private set; }
    public List<string> SentTexts { get; } = new();
    public List<string> SentFiles { get; } = new();
    public List<Guid> Cancelled { get; } = new();

    public Task StartAsync() { Started = true; return Task.CompletedTask; }
    public Task SendTextAsync(string text) { SentTexts.Add(text); return Task.CompletedTask; }

    public Guid NextSendFileId { get; set; } = Guid.NewGuid();
    public Task<Guid> SendFileAsync(string path) { SentFiles.Add(path); return Task.FromResult(NextSendFileId); }

    public Task CancelTransferAsync(Guid id) { Cancelled.Add(id); return Task.CompletedTask; }
    public void Stop() { Stopped = true; }

    public void SetStatus(ConnectionStatus s)
    { Status = s; StatusChanged?.Invoke(s); }
    public void RaiseTextReceived(string t) => TextReceived?.Invoke(t);
    public void RaiseFileOffer(FileOffer o) => FileOfferReceived?.Invoke(o);
    public void RaiseFileProgress(Guid id, long r, long t) => FileProgress?.Invoke(id, r, t);
    public void RaiseFileCompleted(Guid id, string p) => FileCompleted?.Invoke(id, p);
    public void RaiseTransferFailed(Guid id, string r) => TransferFailed?.Invoke(id, r);
}
```

- [ ] **Step 2: Add the failing tests**

Append to `tests/FileTransfer.App.Tests/ViewModels/MainViewModelTests.cs` (and update helper to construct VM with INodeHost):

Replace the `NewVmUnpaired` helper and add `NewVmPaired`:

```csharp
    private static (MainViewModel vm, FakePairingHost host, FakeNodeHost node, ImmediateDispatcher dispatcher) NewVm(bool paired)
    {
        var dispatcher = new ImmediateDispatcher();
        var pairing = new FakePairingHost();
        var node = new FakeNodeHost();
        var vm = new MainViewModel(dispatcher, pairing, node, isPairedOnBoot: paired);
        return (vm, pairing, node, dispatcher);
    }

    private static (MainViewModel vm, FakePairingHost host, ImmediateDispatcher dispatcher) NewVmUnpaired()
    {
        var (vm, pairing, _, dispatcher) = NewVm(paired: false);
        return (vm, pairing, dispatcher);
    }
```

Append tests (use NewVm for the new ones):

```csharp
    [Fact]
    public async Task SendTextCommand_OnPaired_CallsNodeAndAppendsOutgoingBubble()
    {
        var (vm, _, node, _) = NewVm(paired: true);
        await vm.StartAsync();
        vm.InputText = "hello";
        await vm.SendTextCommand.ExecuteAsync(null);
        Assert.Single(node.SentTexts);
        Assert.Equal("hello", node.SentTexts[0]);
        Assert.Single(vm.Messages);
        var msg = Assert.IsType<TextMessageViewModel>(vm.Messages[0]);
        Assert.True(msg.IsOutgoing);
        Assert.Equal("hello", msg.Text);
        Assert.Equal("", vm.InputText);   // cleared after send
    }

    [Fact]
    public async Task SendTextCommand_EmptyInput_DoesNotSend()
    {
        var (vm, _, node, _) = NewVm(paired: true);
        await vm.StartAsync();
        vm.InputText = "   ";
        await vm.SendTextCommand.ExecuteAsync(null);
        Assert.Empty(node.SentTexts);
        Assert.Empty(vm.Messages);
    }

    [Fact]
    public async Task TextReceived_AppendsIncomingBubble()
    {
        var (vm, _, node, _) = NewVm(paired: true);
        await vm.StartAsync();
        node.RaiseTextReceived("hi back");
        Assert.Single(vm.Messages);
        var msg = Assert.IsType<TextMessageViewModel>(vm.Messages[0]);
        Assert.False(msg.IsOutgoing);
        Assert.Equal("hi back", msg.Text);
    }

    [Fact]
    public async Task StatusChanged_Online_SetsAppStateOnline()
    {
        var (vm, _, node, _) = NewVm(paired: true);
        await vm.StartAsync();
        Assert.Equal(AppState.Offline, vm.State);
        node.SetStatus(ConnectionStatus.Online);
        Assert.Equal(AppState.Online, vm.State);
    }

    [Fact]
    public async Task StatusChanged_Offline_FromOnline_GoesBackToOffline()
    {
        var (vm, _, node, _) = NewVm(paired: true);
        await vm.StartAsync();
        node.SetStatus(ConnectionStatus.Online);
        node.SetStatus(ConnectionStatus.Offline);
        Assert.Equal(AppState.Offline, vm.State);
    }
```

- [ ] **Step 3: Verify failing**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: FAIL — `INodeHost`, `Messages`, `InputText`, `SendTextCommand`, `TextMessageViewModel` not implemented; constructor signature mismatch.

- [ ] **Step 4: Implement**

Create `src/FileTransfer.App/ViewModels/TextMessageViewModel.cs`:

```csharp
namespace FileTransfer.App.ViewModels;

public sealed class TextMessageViewModel
{
    public string Text { get; }
    public bool IsOutgoing { get; }
    public DateTime Timestamp { get; }

    public TextMessageViewModel(string text, bool isOutgoing)
    {
        Text = text;
        IsOutgoing = isOutgoing;
        Timestamp = DateTime.Now;
    }
}
```

Replace `src/FileTransfer.App/ViewModels/MainViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTransfer.App.Services;
using FileTransfer.Core;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDispatcher _dispatcher;
    private readonly IPairingHost _pairing;
    private readonly INodeHost _node;

    [ObservableProperty]
    private AppState _state;

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private string _inputText = "";

    public ObservableCollection<DeviceCandidateViewModel> Devices { get; } = new();
    public ObservableCollection<object> Messages { get; } = new();

    public event Action<string /*code*/, string /*peerName*/>? PairingCodeRequested;
    public event Action<PairingResult>? PairingPersisted;

    public MainViewModel(IDispatcher dispatcher, IPairingHost pairing, INodeHost node, bool isPairedOnBoot)
    {
        _dispatcher = dispatcher;
        _pairing = pairing;
        _node = node;
        _state = isPairedOnBoot ? AppState.Offline : AppState.Unpaired;

        _pairing.PeerDiscovered += peer =>
            _dispatcher.Invoke(() => OnPeerDiscovered(peer));
        _pairing.PairingCandidateReady += (code, peer) =>
            _dispatcher.Invoke(() => OnPairingCandidate(code, peer));
        _pairing.PairingCompleted += result =>
            _dispatcher.Invoke(() => OnPairingCompleted(result));
        _pairing.PairingFailed += (reason, detail) =>
            _dispatcher.Invoke(() => OnPairingFailed(reason, detail));

        _node.StatusChanged += s => _dispatcher.Invoke(() => OnStatusChanged(s));
        _node.TextReceived += t => _dispatcher.Invoke(() => OnTextReceived(t));
    }

    public Task StartAsync()
    {
        // The composition root decides which host to actually start based on isPairedOnBoot;
        // here we always invoke both, the fakes/no-ops it injects when unused.
        return Task.WhenAll(_pairing.StartAsync(), _node.StartAsync());
    }

    public Task RespondToPairingAsync(PairingDecision decision) =>
        decision == PairingDecision.Confirmed ? _pairing.ConfirmAsync() : _pairing.RejectAsync();

    [RelayCommand]
    private Task RequestPairing(DeviceCandidateViewModel? candidate)
        => candidate is null ? Task.CompletedTask : _pairing.RequestPairingAsync(candidate.Peer);

    [RelayCommand]
    private async Task SendText()
    {
        var text = InputText;
        if (string.IsNullOrWhiteSpace(text)) return;
        InputText = "";
        Messages.Add(new TextMessageViewModel(text, isOutgoing: true));
        await _node.SendTextAsync(text);
    }

    private void OnPeerDiscovered(PeerCandidate peer)
    {
        foreach (var d in Devices)
            if (d.Fingerprint == peer.Fingerprint) return;
        Devices.Add(new DeviceCandidateViewModel(peer));
    }

    private void OnPairingCandidate(string code, PeerCandidate peer)
    {
        State = AppState.Pairing;
        PairingCodeRequested?.Invoke(code, peer.DeviceName);
    }

    private void OnPairingCompleted(PairingResult result)
    {
        State = AppState.Offline;
        Devices.Clear();
        LastError = null;
        PairingPersisted?.Invoke(result);
    }

    private void OnPairingFailed(PairingFailureReason reason, string detail)
    {
        State = AppState.Unpaired;
        LastError = string.IsNullOrEmpty(detail) ? reason.ToString() : $"{reason}: {detail}";
    }

    private void OnStatusChanged(ConnectionStatus status)
    {
        State = status switch
        {
            ConnectionStatus.Online => AppState.Online,
            ConnectionStatus.Offline => AppState.Offline,
            _ => State,
        };
    }

    private void OnTextReceived(string text)
        => Messages.Add(new TextMessageViewModel(text, isOutgoing: false));
}
```

- [ ] **Step 5: Run**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: PASS (14 tests now).

- [ ] **Step 6: Commit**

```powershell
git add .
git commit -m "feat(app): wire INodeHost, text send/receive, and status to MainViewModel"
```

---

## Task 10: FileMessageViewModel + send-side serial queue

**Files:**
- Create: `src/FileTransfer.App/ViewModels/FileMessageViewModel.cs`
- Modify: `src/FileTransfer.App/ViewModels/MainViewModel.cs`
- Modify: `tests/FileTransfer.App.Tests/ViewModels/MainViewModelTests.cs`
- Create: `tests/FileTransfer.App.Tests/ViewModels/FileMessageViewModelTests.cs`

- [ ] **Step 1: Write tests for FileMessageViewModel state transitions**

Create `tests/FileTransfer.App.Tests/ViewModels/FileMessageViewModelTests.cs`:

```csharp
using FileTransfer.App.ViewModels;

namespace FileTransfer.App.Tests.ViewModels;

public class FileMessageViewModelTests
{
    [Fact]
    public void Constructor_Outgoing_StartsAtSending_With0Progress()
    {
        var vm = new FileMessageViewModel(
            Guid.NewGuid(), name: "doc.pdf", size: 2_400_000,
            mime: "application/pdf", isOutgoing: true);
        Assert.Equal(FileMessageState.Sending, vm.State);
        Assert.Equal(0.0, vm.Progress);
        Assert.True(vm.IsOutgoing);
    }

    [Fact]
    public void Constructor_Incoming_StartsAtReceiving()
    {
        var vm = new FileMessageViewModel(
            Guid.NewGuid(), name: "x.png", size: 100,
            mime: "image/png", isOutgoing: false);
        Assert.Equal(FileMessageState.Receiving, vm.State);
    }

    [Fact]
    public void UpdateProgress_SetsRatioCorrectly()
    {
        var vm = new FileMessageViewModel(
            Guid.NewGuid(), "a", 1000, "application/octet-stream", isOutgoing: true);
        vm.UpdateProgress(received: 250, total: 1000);
        Assert.Equal(0.25, vm.Progress, 3);
    }

    [Fact]
    public void MarkSent_Outgoing_TransitionsToSent()
    {
        var vm = new FileMessageViewModel(
            Guid.NewGuid(), "a", 1000, "application/octet-stream", isOutgoing: true);
        vm.MarkSent();
        Assert.Equal(FileMessageState.Sent, vm.State);
        Assert.Equal(1.0, vm.Progress);
    }

    [Fact]
    public void MarkReceived_SetsStateAndResolvedPath()
    {
        var vm = new FileMessageViewModel(
            Guid.NewGuid(), "a.png", 1000, "image/png", isOutgoing: false);
        vm.MarkReceived(@"C:\Recv\a.png");
        Assert.Equal(FileMessageState.Received, vm.State);
        Assert.Equal(@"C:\Recv\a.png", vm.ResolvedPath);
        Assert.True(vm.IsImage);
    }

    [Fact]
    public void MarkFailed_SetsStateAndReason()
    {
        var vm = new FileMessageViewModel(
            Guid.NewGuid(), "a", 1000, "application/octet-stream", isOutgoing: true);
        vm.MarkFailed("disk full");
        Assert.Equal(FileMessageState.Failed, vm.State);
        Assert.Equal("disk full", vm.FailureReason);
    }
}
```

- [ ] **Step 2: Write tests for MainViewModel send-side queue**

Append to `tests/FileTransfer.App.Tests/ViewModels/MainViewModelTests.cs`:

```csharp
    [Fact]
    public async Task DropFilesCommand_QueuesAllPathsAndSendsSerially()
    {
        var (vm, _, node, _) = NewVm(paired: true);
        await vm.StartAsync();
        node.NextSendFileId = Guid.NewGuid();
        await vm.DropFilesCommand.ExecuteAsync(new[] { @"C:\a.txt", @"C:\b.txt", @"C:\c.txt" });
        // Synchronous fake: all three sends already happened in order.
        Assert.Equal(3, node.SentFiles.Count);
        Assert.Equal(@"C:\a.txt", node.SentFiles[0]);
        Assert.Equal(@"C:\b.txt", node.SentFiles[1]);
        Assert.Equal(@"C:\c.txt", node.SentFiles[2]);
        Assert.Equal(3, vm.Messages.Count);
        Assert.All(vm.Messages, m => Assert.IsType<FileMessageViewModel>(m));
    }

    [Fact]
    public async Task FileProgress_UpdatesMatchingFileMessage()
    {
        var (vm, _, node, _) = NewVm(paired: true);
        await vm.StartAsync();
        var id = Guid.NewGuid();
        node.NextSendFileId = id;
        await vm.DropFilesCommand.ExecuteAsync(new[] { @"C:\big.bin" });
        var fileVm = (FileMessageViewModel)vm.Messages[0];
        node.RaiseFileProgress(id, 500, 1000);
        Assert.Equal(0.5, fileVm.Progress, 3);
    }

    [Fact]
    public async Task CancelTransferCommand_CallsNode()
    {
        var (vm, _, node, _) = NewVm(paired: true);
        await vm.StartAsync();
        var id = Guid.NewGuid();
        node.NextSendFileId = id;
        await vm.DropFilesCommand.ExecuteAsync(new[] { @"C:\big.bin" });
        var fileVm = (FileMessageViewModel)vm.Messages[0];
        fileVm.CancelCommand.Execute(null);
        Assert.Single(node.Cancelled);
        Assert.Equal(id, node.Cancelled[0]);
    }
```

Note: the cancel test needs FileMessageViewModel to have a CancelCommand that calls a callback registered by MainViewModel. We pass this in via the constructor.

- [ ] **Step 3: Verify failing**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests|FullyQualifiedName~FileMessageViewModelTests"`
Expected: FAIL — `FileMessageViewModel`, `DropFilesCommand`, etc. not implemented.

- [ ] **Step 4: Implement FileMessageViewModel**

Create `src/FileTransfer.App/ViewModels/FileMessageViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FileTransfer.App.ViewModels;

public enum FileMessageState { Sending, Sent, Receiving, Received, Cancelled, Failed }

public sealed partial class FileMessageViewModel : ObservableObject
{
    private readonly Func<Guid, Task>? _onCancel;

    public Guid Id { get; }
    public string Name { get; }
    public long Size { get; }
    public string Mime { get; }
    public bool IsOutgoing { get; }
    public DateTime Timestamp { get; }

    [ObservableProperty] private FileMessageState _state;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string? _resolvedPath;
    [ObservableProperty] private string? _failureReason;

    public bool IsImage => Mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                            && State == FileMessageState.Received;

    public FileMessageViewModel(
        Guid id, string name, long size, string mime, bool isOutgoing,
        Func<Guid, Task>? onCancel = null)
    {
        Id = id;
        Name = name;
        Size = size;
        Mime = mime;
        IsOutgoing = isOutgoing;
        Timestamp = DateTime.Now;
        _state = isOutgoing ? FileMessageState.Sending : FileMessageState.Receiving;
        _onCancel = onCancel;
    }

    public void UpdateProgress(long received, long total)
        => Progress = total <= 0 ? 0 : (double)received / total;

    public void MarkSent()
    {
        Progress = 1.0;
        State = FileMessageState.Sent;
    }

    public void MarkReceived(string finalPath)
    {
        Progress = 1.0;
        ResolvedPath = finalPath;
        State = FileMessageState.Received;
        OnPropertyChanged(nameof(IsImage));
    }

    public void MarkFailed(string reason)
    {
        FailureReason = reason;
        State = FileMessageState.Failed;
    }

    public void MarkCancelled() => State = FileMessageState.Cancelled;

    [RelayCommand]
    private Task CancelAsync() => _onCancel?.Invoke(Id) ?? Task.CompletedTask;
}
```

- [ ] **Step 5: Extend MainViewModel with send-side queue**

Add to `MainViewModel.cs` (inside the class, alongside the other fields/methods):

Add field:
```csharp
    private readonly Dictionary<Guid, FileMessageViewModel> _filesById = new();
    private readonly Queue<string> _sendQueue = new();
    private bool _pumpRunning;
```

Add to the constructor (alongside existing _node subscriptions):
```csharp
        _node.FileProgress += (id, recv, total) =>
            _dispatcher.Invoke(() => OnFileProgress(id, recv, total));
        _node.FileCompleted += (id, path) =>
            _dispatcher.Invoke(() => OnFileCompleted(id, path));
        _node.TransferFailed += (id, reason) =>
            _dispatcher.Invoke(() => OnTransferFailed(id, reason));
```

Add commands and handlers:
```csharp
    [RelayCommand]
    private async Task DropFiles(string[]? paths)
    {
        if (paths is null || paths.Length == 0) return;
        foreach (var p in paths) _sendQueue.Enqueue(p);
        await PumpAsync();
    }

    private async Task PumpAsync()
    {
        if (_pumpRunning) return;
        _pumpRunning = true;
        try
        {
            while (_sendQueue.Count > 0)
            {
                var path = _sendQueue.Dequeue();
                var name = Path.GetFileName(path);
                long size;
                try { size = new FileInfo(path).Length; }
                catch { size = 0; }
                var mime = GuessMime(name);

                var id = await _node.SendFileAsync(path);
                var vm = new FileMessageViewModel(id, name, size, mime, isOutgoing: true,
                    onCancel: _node.CancelTransferAsync);
                _filesById[id] = vm;
                Messages.Add(vm);
            }
        }
        finally { _pumpRunning = false; }
    }

    private void OnFileProgress(Guid id, long received, long total)
    {
        if (_filesById.TryGetValue(id, out var vm))
            vm.UpdateProgress(received, total);
    }

    private void OnFileCompleted(Guid id, string finalPath)
    {
        if (!_filesById.TryGetValue(id, out var vm)) return;
        if (vm.IsOutgoing) vm.MarkSent();
        else vm.MarkReceived(finalPath);
    }

    private void OnTransferFailed(Guid id, string reason)
    {
        if (_filesById.TryGetValue(id, out var vm))
            vm.MarkFailed(reason);
    }

    private static string GuessMime(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream",
        };
    }
```

Also add `using System.IO;` at top of file.

- [ ] **Step 6: Run**

Run: `dotnet test --filter "FullyQualifiedName~ViewModels"`
Expected: PASS — file message tests + the 3 new MainViewModel tests + all existing tests.

- [ ] **Step 7: Commit**

```powershell
git add .
git commit -m "feat(app): add FileMessageViewModel and send-side serial file queue"
```

---

## Task 11: Receive-side file handling

**Files:**
- Modify: `src/FileTransfer.App/ViewModels/MainViewModel.cs`
- Modify: `tests/FileTransfer.App.Tests/ViewModels/MainViewModelTests.cs`

- [ ] **Step 1: Append failing tests**

Append to MainViewModelTests:

```csharp
    [Fact]
    public async Task FileOfferReceived_AppendsReceivingBubble()
    {
        var (vm, _, node, _) = NewVm(paired: true);
        await vm.StartAsync();
        var id = Guid.NewGuid();
        node.RaiseFileOffer(new FileTransfer.Core.Protocol.FileOffer
        {
            Id = id, Name = "incoming.bin", Size = 5000, Mime = "application/octet-stream"
        });
        Assert.Single(vm.Messages);
        var fileVm = Assert.IsType<FileMessageViewModel>(vm.Messages[0]);
        Assert.False(fileVm.IsOutgoing);
        Assert.Equal(FileMessageState.Receiving, fileVm.State);
        Assert.Equal("incoming.bin", fileVm.Name);
    }

    [Fact]
    public async Task FileCompleted_OnReceive_SetsReceivedWithPath()
    {
        var (vm, _, node, _) = NewVm(paired: true);
        await vm.StartAsync();
        var id = Guid.NewGuid();
        node.RaiseFileOffer(new FileTransfer.Core.Protocol.FileOffer
        {
            Id = id, Name = "incoming.bin", Size = 5000, Mime = "application/octet-stream"
        });
        node.RaiseFileCompleted(id, @"C:\Recv\incoming.bin");
        var fileVm = (FileMessageViewModel)vm.Messages[0];
        Assert.Equal(FileMessageState.Received, fileVm.State);
        Assert.Equal(@"C:\Recv\incoming.bin", fileVm.ResolvedPath);
    }

    [Fact]
    public async Task TransferFailed_OnReceive_SetsFailedWithReason()
    {
        var (vm, _, node, _) = NewVm(paired: true);
        await vm.StartAsync();
        var id = Guid.NewGuid();
        node.RaiseFileOffer(new FileTransfer.Core.Protocol.FileOffer
        {
            Id = id, Name = "incoming.bin", Size = 5000, Mime = "application/octet-stream"
        });
        node.RaiseTransferFailed(id, "disk full");
        var fileVm = (FileMessageViewModel)vm.Messages[0];
        Assert.Equal(FileMessageState.Failed, fileVm.State);
        Assert.Equal("disk full", fileVm.FailureReason);
    }
```

- [ ] **Step 2: Verify failing**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: FAIL — `FileOfferReceived` not yet hooked.

- [ ] **Step 3: Implement**

In `MainViewModel.cs`, add to the constructor (alongside other _node subscriptions):

```csharp
        _node.FileOfferReceived += offer =>
            _dispatcher.Invoke(() => OnFileOfferReceived(offer));
```

Add the handler:

```csharp
    private void OnFileOfferReceived(FileTransfer.Core.Protocol.FileOffer offer)
    {
        var vm = new FileMessageViewModel(offer.Id, offer.Name, offer.Size, offer.Mime, isOutgoing: false);
        _filesById[offer.Id] = vm;
        Messages.Add(vm);
    }
```

- [ ] **Step 4: Run**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(app): handle inbound file offers, progress, completion, and failure"
```

---

## Task 12: Clipboard image paste + IsImage smoke

**Files:**
- Modify: `src/FileTransfer.App/ViewModels/MainViewModel.cs`
- Modify: `tests/FileTransfer.App.Tests/ViewModels/MainViewModelTests.cs`

- [ ] **Step 1: Wire IClipboard into MainViewModel**

Modify the helper `NewVm` to inject a `FakeClipboard`:

```csharp
    private static (MainViewModel vm, FakePairingHost host, FakeNodeHost node,
                    FakeClipboard clipboard, ImmediateDispatcher dispatcher) NewVm(bool paired)
    {
        var dispatcher = new ImmediateDispatcher();
        var pairing = new FakePairingHost();
        var node = new FakeNodeHost();
        var clipboard = new FakeClipboard();
        var vm = new MainViewModel(dispatcher, pairing, node, clipboard, isPairedOnBoot: paired);
        return (vm, pairing, node, clipboard, dispatcher);
    }
```

Update old call sites to discard the clipboard with `_`.

Add tests:

```csharp
    [Fact]
    public async Task PasteImageCommand_WithClipboardImage_EnqueuesAsFile()
    {
        var (vm, _, node, clipboard, _) = NewVm(paired: true);
        await vm.StartAsync();
        clipboard.NextResult = @"C:\Temp\screenshot.png";
        await vm.PasteImageCommand.ExecuteAsync(null);
        Assert.Single(node.SentFiles);
        Assert.Equal(@"C:\Temp\screenshot.png", node.SentFiles[0]);
        Assert.Equal(1, clipboard.CallCount);
    }

    [Fact]
    public async Task PasteImageCommand_NoImage_NoOp()
    {
        var (vm, _, node, clipboard, _) = NewVm(paired: true);
        await vm.StartAsync();
        clipboard.NextResult = null;
        await vm.PasteImageCommand.ExecuteAsync(null);
        Assert.Empty(node.SentFiles);
    }

    [Fact]
    public async Task ReceivedImageFile_HasIsImageTrue()
    {
        var (vm, _, node, _, _) = NewVm(paired: true);
        await vm.StartAsync();
        var id = Guid.NewGuid();
        node.RaiseFileOffer(new FileTransfer.Core.Protocol.FileOffer
        {
            Id = id, Name = "shot.png", Size = 1000, Mime = "image/png"
        });
        node.RaiseFileCompleted(id, @"C:\Recv\shot.png");
        var fileVm = (FileMessageViewModel)vm.Messages[0];
        Assert.True(fileVm.IsImage);
    }
```

- [ ] **Step 2: Verify failing**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: FAIL — `PasteImageCommand` and clipboard ctor param don't exist.

- [ ] **Step 3: Implement**

Modify `MainViewModel.cs`:

Add field:
```csharp
    private readonly IClipboard _clipboard;
```

Modify constructor to take `IClipboard clipboard` and assign `_clipboard = clipboard;`.

Add command:
```csharp
    [RelayCommand]
    private async Task PasteImage()
    {
        var path = _clipboard.GrabImageAsPng();
        if (path is null) return;
        _sendQueue.Enqueue(path);
        await PumpAsync();
    }
```

Update `using` block: add `using FileTransfer.App.Services;`.

- [ ] **Step 4: Run**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(app): add clipboard image paste command"
```

---

## Task 13: SettingsViewModel + unpair flow

**Files:**
- Create: `src/FileTransfer.App/ViewModels/SettingsViewModel.cs`
- Create: `tests/FileTransfer.App.Tests/ViewModels/SettingsViewModelTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/FileTransfer.App.Tests/ViewModels/SettingsViewModelTests.cs`:

```csharp
using FileTransfer.App.Services;
using FileTransfer.App.Tests.Fakes;
using FileTransfer.App.ViewModels;

namespace FileTransfer.App.Tests.ViewModels;

public class SettingsViewModelTests
{
    private static (SettingsViewModel vm, FakeFolderPicker folder, FakeAutoStartRegistry reg)
        NewVm(string deviceName, string receiveDir, bool autoStart, string ownFp)
    {
        var folder = new FakeFolderPicker();
        var reg = new FakeAutoStartRegistry { Enabled = autoStart };
        var vm = new SettingsViewModel(folder, reg, executablePath: @"C:\App\FileTransfer.App.exe")
        {
            DeviceName = deviceName,
            ReceiveDirectory = receiveDir,
            AutoStart = autoStart,
            OwnFingerprint = ownFp,
        };
        return (vm, folder, reg);
    }

    [Fact]
    public void Constructor_ExposesInjectedValues()
    {
        var (vm, _, _) = NewVm("MyPC", @"C:\Downloads", autoStart: true, "ABCD");
        Assert.Equal("MyPC", vm.DeviceName);
        Assert.Equal(@"C:\Downloads", vm.ReceiveDirectory);
        Assert.True(vm.AutoStart);
        Assert.Equal("ABCD", vm.OwnFingerprint);
    }

    [Fact]
    public async Task BrowseReceiveDirectoryCommand_UpdatesPathOnSelection()
    {
        var (vm, folder, _) = NewVm("MyPC", @"C:\Downloads", false, "ABCD");
        folder.NextResult = @"C:\NewDir";
        await vm.BrowseReceiveDirectoryCommand.ExecuteAsync(null);
        Assert.Equal(@"C:\NewDir", vm.ReceiveDirectory);
    }

    [Fact]
    public async Task BrowseReceiveDirectoryCommand_NoChangeIfCancelled()
    {
        var (vm, folder, _) = NewVm("MyPC", @"C:\Downloads", false, "ABCD");
        folder.NextResult = null;
        await vm.BrowseReceiveDirectoryCommand.ExecuteAsync(null);
        Assert.Equal(@"C:\Downloads", vm.ReceiveDirectory);
    }

    [Fact]
    public void Save_AutoStartTrue_EnablesRegistry()
    {
        var (vm, _, reg) = NewVm("MyPC", @"C:\Downloads", autoStart: false, "ABCD");
        vm.AutoStart = true;
        vm.ApplyAutoStart();
        Assert.True(reg.IsEnabled());
        Assert.Equal(@"C:\App\FileTransfer.App.exe", reg.EnabledPath);
    }

    [Fact]
    public void Save_AutoStartFalse_DisablesRegistry()
    {
        var (vm, _, reg) = NewVm("MyPC", @"C:\Downloads", autoStart: true, "ABCD");
        reg.Enable(@"C:\App\FileTransfer.App.exe");
        vm.AutoStart = false;
        vm.ApplyAutoStart();
        Assert.False(reg.IsEnabled());
    }

    [Fact]
    public void UnpairCommand_RaisesUnpairRequestedEvent()
    {
        var (vm, _, _) = NewVm("MyPC", @"C:\Downloads", false, "ABCD");
        bool fired = false;
        vm.UnpairRequested += () => fired = true;
        vm.UnpairCommand.Execute(null);
        Assert.True(fired);
    }
}
```

- [ ] **Step 2: Verify failing**

Run: `dotnet test --filter "FullyQualifiedName~SettingsViewModelTests"`
Expected: FAIL — SettingsViewModel doesn't exist.

- [ ] **Step 3: Implement**

Create `src/FileTransfer.App/ViewModels/SettingsViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileTransfer.App.Services;

namespace FileTransfer.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IFolderPicker _folderPicker;
    private readonly IAutoStartRegistry _autoStart;
    private readonly string _executablePath;

    [ObservableProperty] private string _deviceName = "";
    [ObservableProperty] private string _receiveDirectory = "";
    [ObservableProperty] private bool _autoStart;
    [ObservableProperty] private string _ownFingerprint = "";

    public event Action? UnpairRequested;

    public SettingsViewModel(IFolderPicker folderPicker, IAutoStartRegistry autoStart, string executablePath)
    {
        _folderPicker = folderPicker;
        _autoStart = autoStart;
        _executablePath = executablePath;
    }

    [RelayCommand]
    private async Task BrowseReceiveDirectory()
    {
        var chosen = await _folderPicker.PickAsync(initialDirectory: ReceiveDirectory);
        if (chosen is not null) ReceiveDirectory = chosen;
    }

    /// Called by the View when Save is confirmed; writes the auto-start registry flag
    /// based on the current property value.
    public void ApplyAutoStart()
    {
        if (AutoStart) _autoStart.Enable(_executablePath);
        else _autoStart.Disable();
    }

    [RelayCommand]
    private void Unpair() => UnpairRequested?.Invoke();
}
```

- [ ] **Step 4: Run**

Run: `dotnet test --filter "FullyQualifiedName~SettingsViewModelTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(app): add SettingsViewModel for device name, receive dir, auto-start, unpair"
```

---

## Task 14: App.xaml.cs Composition Root + production INodeHost/IPairingHost adapters

**Files:**
- Create: `src/FileTransfer.App/Composition/PairingServiceHost.cs`
- Create: `src/FileTransfer.App/Composition/NodeHost.cs`
- Create: `src/FileTransfer.App/Composition/BootSequence.cs`
- Modify: `src/FileTransfer.App/App.xaml.cs`

This is the integration task: wire real `Node` and `PairingService` from Core to the IHost interfaces, then in App.xaml.cs build everything and show MainWindow.

- [ ] **Step 1: Implement the adapters**

Create `src/FileTransfer.App/Composition/PairingServiceHost.cs`:

```csharp
using FileTransfer.App.ViewModels;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App.Composition;

/// IPairingHost that owns a real PairingService.
public sealed class PairingServiceHost : IPairingHost, IDisposable
{
    private readonly PairingService _svc;

    public event Action<PeerCandidate>? PeerDiscovered;
    public event Action<string, PeerCandidate>? PairingCandidateReady;
    public event Action<PairingResult>? PairingCompleted;
    public event Action<PairingFailureReason, string>? PairingFailed;

    public PairingServiceHost(PairingServiceOptions options)
    {
        _svc = new PairingService(options);
        _svc.PeerDiscovered += p => PeerDiscovered?.Invoke(p);
        _svc.PairingCandidateReady += (code, p) => PairingCandidateReady?.Invoke(code, p);
        _svc.PairingCompleted += r => PairingCompleted?.Invoke(r);
        _svc.PairingFailed += (reason, msg) => PairingFailed?.Invoke(reason, msg);
    }

    public string OwnFingerprint => _svc.OwnFingerprint;
    public Task StartAsync() => _svc.StartAsync();
    public Task RequestPairingAsync(PeerCandidate peer) => _svc.RequestPairingAsync(peer);
    public Task ConfirmAsync() => _svc.ConfirmAsync();
    public Task RejectAsync(string reason = "") => _svc.RejectAsync(reason);
    public void Dispose() => _svc.Dispose();
}
```

Create `src/FileTransfer.App/Composition/NodeHost.cs`:

```csharp
using FileTransfer.App.ViewModels;
using FileTransfer.Core;
using FileTransfer.Core.Protocol;

namespace FileTransfer.App.Composition;

public sealed class NodeHost : INodeHost, IDisposable
{
    private readonly Node _node;

    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<string>? TextReceived;
    public event Action<FileOffer>? FileOfferReceived;
    public event Action<Guid, long, long>? FileProgress;
    public event Action<Guid, string>? FileCompleted;
    public event Action<Guid, string>? TransferFailed;

    public ConnectionStatus Status => _node.Status;
    public string PeerName => _node.PeerName;

    public NodeHost(NodeOptions options)
    {
        _node = new Node(options);
        _node.StatusChanged += s => StatusChanged?.Invoke(s);
        _node.TextReceived += t => TextReceived?.Invoke(t);
        _node.FileOfferReceived += o => FileOfferReceived?.Invoke(o);
        _node.FileProgress += (id, r, t) => FileProgress?.Invoke(id, r, t);
        _node.FileCompleted += (id, p) => FileCompleted?.Invoke(id, p);
        _node.TransferFailed += (id, r) => TransferFailed?.Invoke(id, r);
    }

    public string OwnFingerprint => FileTransfer.Core.Crypto.Fingerprint.Compute(_node.OwnCertificateRawData);
    public Task StartAsync() => _node.StartAsync();
    public Task SendTextAsync(string text) => _node.SendTextAsync(text);
    public Task<Guid> SendFileAsync(string path) => _node.SendFileAsync(path);
    public Task CancelTransferAsync(Guid id) => _node.CancelTransferAsync(id);
    public void Stop() => _node.Stop();
    public void Dispose() => _node.Dispose();
}
```

> Note: `Node` does not expose `OwnCertificateRawData` today. If the property is missing, instead pass the fingerprint into NodeHost via constructor (computed once from the cert that was used to build NodeOptions). Adjust both `NodeHost` and the BootSequence below if the Core API doesn't already expose this.

Adjustment: NodeHost takes the precomputed fingerprint:

```csharp
    private readonly string _ownFingerprint;
    public NodeHost(NodeOptions options, string ownFingerprint)
    {
        _node = new Node(options);
        _ownFingerprint = ownFingerprint;
        // ... event wiring as above
    }
    public string OwnFingerprint => _ownFingerprint;
```

- [ ] **Step 2: Implement BootSequence (the AppConfig + cert loading sequence)**

Create `src/FileTransfer.App/Composition/BootSequence.cs`:

```csharp
using System.IO;
using FileTransfer.App.ViewModels;
using FileTransfer.Core.Config;
using FileTransfer.Core.Crypto;
using FileTransfer.Core.Pairing;
using FileTransfer.Core;

namespace FileTransfer.App.Composition;

public sealed record BootArtifacts(
    AppConfig Config,
    string ConfigPath,
    ISecretProtector Protector,
    IPairingHost? PairingHost,
    INodeHost? NodeHost,
    bool IsPaired);

public static class BootSequence
{
    public static BootArtifacts Build(ISecretProtector protector)
    {
        var configPath = AppConfig.DefaultPath;
        var config = AppConfig.Load(configPath, protector) ?? CreateInitialConfig(protector, configPath);

        if (config.IsPaired)
        {
            using var cert = config.GetCertificate(protector);
            var fp = Fingerprint.Compute(cert.RawData);
            var node = new NodeHost(new NodeOptions
            {
                DeviceName = config.DeviceName,
                OwnCertificate = config.GetCertificate(protector),
                PeerFingerprint = config.PeerFingerprint!,
                ReceiveDirectory = config.ReceiveDirectory,
            }, fp);
            return new BootArtifacts(config, configPath, protector, null, node, IsPaired: true);
        }
        else
        {
            var pairing = new PairingServiceHost(new PairingServiceOptions
            {
                DeviceName = config.DeviceName,
                OwnCertificate = config.GetCertificate(protector),
            });
            return new BootArtifacts(config, configPath, protector, pairing, null, IsPaired: false);
        }
    }

    private static AppConfig CreateInitialConfig(ISecretProtector protector, string path)
    {
        var config = new AppConfig();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var cert = CertificateFactory.CreateSelfSigned($"FileTransfer-{config.DeviceName}");
        config.SetCertificate(cert, protector);
        config.Save(path, protector);
        return config;
    }
}
```

- [ ] **Step 3: Skip automated tests for BootSequence**

BootSequence touches `AppConfig.DefaultPath` (a fixed `%APPDATA%\FileTransfer\config.json` path) and the file system, so unit-testing it cleanly would require adding a path-injection knob to `AppConfig` — out of scope for this plan. BootSequence is verified by the manual smoke checklist (Task 16). No automated test file is created.

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build`
Expected: 0 errors. (BootSequenceTests above is empty — that's fine; manual smoke covers boot.)

- [ ] **Step 5: Wire App.xaml.cs**

Replace `src/FileTransfer.App/App.xaml.cs`:

```csharp
using System.Windows;
using FileTransfer.App.Composition;
using FileTransfer.App.Services;
using FileTransfer.App.ViewModels;
using FileTransfer.Core.Config;
using FileTransfer.Core.Pairing;

namespace FileTransfer.App;

public partial class App : Application
{
    private BootArtifacts? _boot;
    private MainViewModel? _mainVm;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            ISecretProtector protector = new DpapiProtector();
            _boot = BootSequence.Build(protector);

            var dispatcher = new WpfDispatcher(Dispatcher);
            var pairing = _boot.PairingHost ?? new NullPairingHost();
            var node = _boot.NodeHost ?? new NullNodeHost();
            var clipboard = new WpfClipboard();

            _mainVm = new MainViewModel(dispatcher, pairing, node, clipboard, _boot.IsPaired);
            _mainVm.PairingCodeRequested += (code, peerName) => ShowPairingDialog(code, peerName);
            _mainVm.PairingPersisted += result => OnPairingPersisted(result);

            var window = new MainWindow { DataContext = _mainVm };
            window.Show();
            await _mainVm.StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup failed:\n{ex.Message}", "FileTransfer", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ShowPairingDialog(string code, string peerName)
    {
        var vm = new PairingCodeDialogViewModel(code, peerName);
        var dialog = new PairingCodeDialog { DataContext = vm, Owner = MainWindow };
        dialog.ShowDialog();
        var decision = vm.Decision ?? PairingDecision.Rejected;
        _ = _mainVm!.RespondToPairingAsync(decision);
    }

    private void OnPairingPersisted(PairingResult result)
    {
        // Persist to config and restart hosts to switch from pairing → node.
        var protector = _boot!.Protector;
        _boot.Config.PeerFingerprint = result.PeerFingerprint;
        _boot.Config.PeerDeviceName = result.PeerDeviceName;
        _boot.Config.Save(_boot.ConfigPath, protector);
        // Tear down pairing host; build node host. For v1 we rebuild MainViewModel.
        MessageBox.Show($"Paired with {result.PeerDeviceName}. Please restart the app to start chatting.",
            "FileTransfer", MessageBoxButton.OK, MessageBoxImage.Information);
        // Future improvement: hot-swap hosts without a restart. v1 prompts user.
    }

    protected override void OnExit(ExitEventArgs e)
    {
        (_boot?.PairingHost as IDisposable)?.Dispose();
        (_boot?.NodeHost as IDisposable)?.Dispose();
        base.OnExit(e);
    }
}

/// No-op fallback host used when the alternate mode isn't active (e.g., NullNodeHost when
/// unpaired). Lets MainViewModel keep its single constructor.
file sealed class NullPairingHost : IPairingHost
{
    public event Action<PeerCandidate>? PeerDiscovered;
    public event Action<string, PeerCandidate>? PairingCandidateReady;
    public event Action<PairingResult>? PairingCompleted;
    public event Action<PairingFailureReason, string>? PairingFailed;
    public Task StartAsync() => Task.CompletedTask;
    public Task RequestPairingAsync(PeerCandidate peer) => Task.CompletedTask;
    public Task ConfirmAsync() => Task.CompletedTask;
    public Task RejectAsync(string reason = "") => Task.CompletedTask;
}

file sealed class NullNodeHost : INodeHost
{
    public FileTransfer.Core.ConnectionStatus Status => FileTransfer.Core.ConnectionStatus.Offline;
    public string PeerName => "";
    public event Action<FileTransfer.Core.ConnectionStatus>? StatusChanged;
    public event Action<string>? TextReceived;
    public event Action<FileTransfer.Core.Protocol.FileOffer>? FileOfferReceived;
    public event Action<Guid, long, long>? FileProgress;
    public event Action<Guid, string>? FileCompleted;
    public event Action<Guid, string>? TransferFailed;
    public Task StartAsync() => Task.CompletedTask;
    public Task SendTextAsync(string text) => Task.CompletedTask;
    public Task<Guid> SendFileAsync(string path) => Task.FromResult(Guid.Empty);
    public Task CancelTransferAsync(Guid id) => Task.CompletedTask;
    public void Stop() { }
}
```

- [ ] **Step 6: Build**

Run: `dotnet build`
Expected: 0 errors.

- [ ] **Step 7: Commit**

```powershell
git add .
git commit -m "feat(app): add PairingServiceHost, NodeHost, BootSequence, and Composition Root"
```

---

## Task 15: MainWindow + UnpairedView + PairedView + dialogs XAML

**Files:**
- Modify: `src/FileTransfer.App/MainWindow.xaml`
- Create: `src/FileTransfer.App/Views/UnpairedView.xaml`, `.cs`
- Create: `src/FileTransfer.App/Views/PairedView.xaml`, `.cs`
- Create: `src/FileTransfer.App/Views/PairingCodeDialog.xaml`, `.cs`
- Create: `src/FileTransfer.App/Views/SettingsDialog.xaml`, `.cs`
- Create: `src/FileTransfer.App/Converters/FileSizeConverter.cs`
- Create: `src/FileTransfer.App/Converters/BoolToVisibilityConverter.cs`

This task is XAML-heavy; no automated tests (XAML is verified by manual smoke at Task 16).

- [ ] **Step 1: Converters**

Create `src/FileTransfer.App/Converters/FileSizeConverter.cs`:

```csharp
using System.Globalization;
using System.Windows.Data;

namespace FileTransfer.App.Converters;

public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bytes) return "";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.##} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

Create `src/FileTransfer.App/Converters/BoolToVisibilityConverter.cs`:

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FileTransfer.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

- [ ] **Step 2: MainWindow.xaml with state-driven content**

Replace `src/FileTransfer.App/MainWindow.xaml`:

```xml
<Window x:Class="FileTransfer.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:FileTransfer.App.Views"
        xmlns:vm="clr-namespace:FileTransfer.App.ViewModels"
        Title="File Transfer" Height="600" Width="450"
        AllowDrop="True" Drop="OnFilesDropped">
    <Window.Resources>
        <DataTemplate x:Key="UnpairedTemplate">
            <views:UnpairedView/>
        </DataTemplate>
        <DataTemplate x:Key="PairedTemplate">
            <views:PairedView/>
        </DataTemplate>
    </Window.Resources>
    <Grid>
        <ContentControl>
            <ContentControl.Style>
                <Style TargetType="ContentControl">
                    <Setter Property="ContentTemplate" Value="{StaticResource PairedTemplate}"/>
                    <Style.Triggers>
                        <DataTrigger Binding="{Binding State}" Value="{x:Static vm:AppState.Unpaired}">
                            <Setter Property="ContentTemplate" Value="{StaticResource UnpairedTemplate}"/>
                        </DataTrigger>
                        <DataTrigger Binding="{Binding State}" Value="{x:Static vm:AppState.Pairing}">
                            <Setter Property="ContentTemplate" Value="{StaticResource UnpairedTemplate}"/>
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </ContentControl.Style>
        </ContentControl>
    </Grid>
</Window>
```

Update `src/FileTransfer.App/MainWindow.xaml.cs` to handle drop:

```csharp
using System.Windows;
using FileTransfer.App.ViewModels;

namespace FileTransfer.App;

public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); }

    private async void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        await vm.DropFilesCommand.ExecuteAsync(paths);
    }
}
```

- [ ] **Step 3: UnpairedView**

Create `src/FileTransfer.App/Views/UnpairedView.xaml`:

```xml
<UserControl x:Class="FileTransfer.App.Views.UnpairedView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <TextBlock Text="搜索附近设备..." FontSize="16" Margin="0,0,0,8"/>
        <ListBox Grid.Row="1" ItemsSource="{Binding Devices}">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel>
                            <TextBlock Text="{Binding DeviceName}" FontWeight="Bold"/>
                            <TextBlock Text="{Binding Fingerprint}" FontSize="10" Foreground="Gray"/>
                        </StackPanel>
                        <Button Grid.Column="1" Content="配对"
                                Command="{Binding DataContext.RequestPairingCommand,
                                                  RelativeSource={RelativeSource AncestorType=UserControl}}"
                                CommandParameter="{Binding}"/>
                    </Grid>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>
        <TextBlock Grid.Row="2" Foreground="Red" Margin="0,8,0,0"
                   Text="{Binding LastError}"
                   Visibility="{Binding LastError, Converter={StaticResource NullToCollapsedConverter}}"/>
    </Grid>
</UserControl>
```

(For now, omit the NullToCollapsedConverter — leave the TextBlock always visible with empty text, or use BoolToVisibility on a calculated property. Simplest: bind directly and accept empty TextBlock when LastError is null. Update XAML to remove the Visibility binding.)

Simplified version:

```xml
        <TextBlock Grid.Row="2" Foreground="Red" Margin="0,8,0,0"
                   Text="{Binding LastError}"/>
```

Create `src/FileTransfer.App/Views/UnpairedView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace FileTransfer.App.Views;

public partial class UnpairedView : UserControl
{
    public UnpairedView() { InitializeComponent(); }
}
```

- [ ] **Step 4: PairedView**

Create `src/FileTransfer.App/Views/PairedView.xaml`:

```xml
<UserControl x:Class="FileTransfer.App.Views.PairedView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:FileTransfer.App.ViewModels">
    <UserControl.Resources>
        <DataTemplate DataType="{x:Type vm:TextMessageViewModel}">
            <Border Margin="4" Padding="8" CornerRadius="6" Background="LightGray">
                <TextBlock Text="{Binding Text}" TextWrapping="Wrap"/>
            </Border>
        </DataTemplate>
        <DataTemplate DataType="{x:Type vm:FileMessageViewModel}">
            <Border Margin="4" Padding="8" CornerRadius="6" Background="WhiteSmoke">
                <StackPanel>
                    <TextBlock Text="{Binding Name}" FontWeight="Bold"/>
                    <TextBlock>
                        <Run Text="{Binding Size}"/>
                        <Run Text=" bytes"/>
                    </TextBlock>
                    <ProgressBar Minimum="0" Maximum="1" Value="{Binding Progress}" Height="8"/>
                    <TextBlock Text="{Binding State}"/>
                    <Button Content="取消" Command="{Binding CancelCommand}"
                            Visibility="{Binding ShowCancelButton, Converter={StaticResource BoolToVis}}"/>
                </StackPanel>
            </Border>
        </DataTemplate>
    </UserControl.Resources>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <DockPanel Grid.Row="0" Background="LightBlue">
            <TextBlock Text="{Binding State}" Margin="8"/>
            <TextBlock Text="对方: " Margin="8,8,0,8"/>
            <TextBlock Text="{Binding PeerName}" Margin="0,8,8,8"/>
            <Button Content="⚙" DockPanel.Dock="Right" Command="{Binding OpenSettingsCommand}" Width="32"/>
        </DockPanel>
        <ListBox Grid.Row="1" ItemsSource="{Binding Messages}"/>
        <Grid Grid.Row="2" Margin="8">
            <Grid.RowDefinitions>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>
            <TextBox Grid.Row="0" Text="{Binding InputText, UpdateSourceTrigger=PropertyChanged}"
                     AcceptsReturn="False" MinHeight="32"
                     KeyDown="OnInputKeyDown"/>
            <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,4,0,0">
                <Button Content="📎 文件" Command="{Binding PickFileCommand}" Margin="0,0,8,0"/>
                <Button Content="发送" Command="{Binding SendTextCommand}"/>
            </StackPanel>
        </Grid>
    </Grid>
</UserControl>
```

> Note: `ShowCancelButton`, `OpenSettingsCommand`, `PickFileCommand` are referenced but not yet implemented in MainViewModel. Add them now as small additions:
>
> In `FileMessageViewModel.cs` add `public bool ShowCancelButton => State == FileMessageState.Sending || State == FileMessageState.Receiving;` and raise PropertyChanged on it from state changes.
>
> In `MainViewModel.cs` add `[RelayCommand] private async Task OpenSettings() { /* raised as event for App.xaml.cs */ SettingsRequested?.Invoke(); }` and `[RelayCommand] private async Task PickFile() { /* delegates to IFilePicker via event or held service */ }` — see Step 5.

Create `src/FileTransfer.App/Views/PairedView.xaml.cs`:

```csharp
using System.Windows.Controls;
using System.Windows.Input;
using FileTransfer.App.ViewModels;

namespace FileTransfer.App.Views;

public partial class PairedView : UserControl
{
    public PairedView() { InitializeComponent(); }

    private async void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
        if (DataContext is not MainViewModel vm) return;
        e.Handled = true;
        await vm.SendTextCommand.ExecuteAsync(null);
    }
}
```

Add `App.xaml` resource for the converter:

```xml
<Application.Resources>
    <ResourceDictionary>
        <conv:BoolToVisibilityConverter x:Key="BoolToVis"
            xmlns:conv="clr-namespace:FileTransfer.App.Converters"/>
    </ResourceDictionary>
</Application.Resources>
```

- [ ] **Step 5: Add PickFileCommand / SettingsRequested event / ShowCancelButton**

Modify `MainViewModel.cs`:

Add field:
```csharp
    private readonly IFilePicker _filePicker;
```

Modify constructor signature to take `IFilePicker filePicker` and store it. Adjust all test helper to pass a FakeFilePicker. Add to App.xaml.cs OnStartup: `var filePicker = new WpfFilePicker();`.

Add command + event:

```csharp
    public event Action? SettingsRequested;

    [RelayCommand]
    private async Task PickFile()
    {
        var picked = await _filePicker.PickAsync();
        if (picked.Count == 0) return;
        foreach (var p in picked) _sendQueue.Enqueue(p);
        await PumpAsync();
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke();
```

Modify `FileMessageViewModel.cs`:

```csharp
    public bool ShowCancelButton => State is FileMessageState.Sending or FileMessageState.Receiving;

    partial void OnStateChanged(FileMessageState value) => OnPropertyChanged(nameof(ShowCancelButton));
```

Add MainViewModelTests for PickFileCommand to round out coverage:

```csharp
    [Fact]
    public async Task PickFileCommand_EnqueuesPickedPaths()
    {
        var (vm, _, node, _, _, picker) = NewVm(paired: true); // helper updated to include picker
        await vm.StartAsync();
        picker.NextResult = new[] { @"C:\a.txt", @"C:\b.txt" };
        await vm.PickFileCommand.ExecuteAsync(null);
        Assert.Equal(2, node.SentFiles.Count);
    }
```

Update `NewVm` helper to include FakeFilePicker.

- [ ] **Step 6: PairingCodeDialog + SettingsDialog**

Create `src/FileTransfer.App/Views/PairingCodeDialog.xaml`:

```xml
<Window x:Class="FileTransfer.App.Views.PairingCodeDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="配对确认" Height="220" Width="320"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <TextBlock Text="请在两台设备上确认配对码" FontSize="14" TextWrapping="Wrap"/>
        <TextBlock Grid.Row="1" Text="{Binding Code}" FontSize="48"
                   HorizontalAlignment="Center" VerticalAlignment="Center" FontWeight="Bold"/>
        <StackPanel Grid.Row="2" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="取消" Command="{Binding RejectCommand}" Click="OnRejectClicked" Width="80" Margin="0,0,8,0"/>
            <Button Content="确认" Command="{Binding ConfirmCommand}" Click="OnConfirmClicked" Width="80" IsDefault="True"/>
        </StackPanel>
    </Grid>
</Window>
```

Create `src/FileTransfer.App/Views/PairingCodeDialog.xaml.cs`:

```csharp
using System.Windows;

namespace FileTransfer.App.Views;

public partial class PairingCodeDialog : Window
{
    public PairingCodeDialog() { InitializeComponent(); }
    private void OnConfirmClicked(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void OnRejectClicked(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
```

Create `src/FileTransfer.App/Views/SettingsDialog.xaml`:

```xml
<Window x:Class="FileTransfer.App.Views.SettingsDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="设置" Height="420" Width="420"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <TextBlock Text="设备名" Margin="0,0,0,4"/>
        <TextBox Grid.Row="1" Text="{Binding DeviceName, UpdateSourceTrigger=PropertyChanged}" Margin="0,0,0,12"/>
        <TextBlock Grid.Row="2" Text="接收目录" Margin="0,0,0,4"/>
        <DockPanel Grid.Row="3" Margin="0,0,0,12">
            <Button Content="浏览..." DockPanel.Dock="Right" Command="{Binding BrowseReceiveDirectoryCommand}" Margin="8,0,0,0"/>
            <TextBox Text="{Binding ReceiveDirectory, UpdateSourceTrigger=PropertyChanged}"/>
        </DockPanel>
        <CheckBox Grid.Row="4" Content="开机自启" IsChecked="{Binding AutoStart}" Margin="0,0,0,12"/>
        <StackPanel Grid.Row="5" VerticalAlignment="Top">
            <TextBlock Text="本机指纹(调试用)" Margin="0,0,0,4"/>
            <TextBox Text="{Binding OwnFingerprint, Mode=OneWay}" IsReadOnly="True" FontFamily="Consolas" Margin="0,0,0,12"/>
            <Button Content="取消配对" Command="{Binding UnpairCommand}" HorizontalAlignment="Left" Width="100"/>
        </StackPanel>
        <StackPanel Grid.Row="6" Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="取消" Click="OnCancelClicked" Width="80" Margin="0,0,8,0"/>
            <Button Content="保存" Click="OnSaveClicked" Width="80" IsDefault="True"/>
        </StackPanel>
    </Grid>
</Window>
```

Create `src/FileTransfer.App/Views/SettingsDialog.xaml.cs`:

```csharp
using System.Windows;
using FileTransfer.App.ViewModels;

namespace FileTransfer.App.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog() { InitializeComponent(); }
    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm) vm.ApplyAutoStart();
        DialogResult = true;
        Close();
    }
    private void OnCancelClicked(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
```

- [ ] **Step 7: Build + run tests**

Run: `dotnet build` then `dotnet test`
Expected: 0 errors; all ViewModel tests pass.

- [ ] **Step 8: Commit**

```powershell
git add .
git commit -m "feat(app): add MainWindow, UnpairedView, PairedView, dialogs, and converters"
```

---

## Task 16: Manual smoke checklist + README

**Files:**
- Create: `README.md` (at repo root if not present, otherwise extend)
- Create: `docs/smoke-checklist.md`

This task documents how to verify the app end-to-end on a real machine pair and produces a checklist a maintainer can run on each release.

- [ ] **Step 1: Author the smoke checklist**

Create `docs/smoke-checklist.md`:

```markdown
# FileTransfer.App Smoke Checklist

Run this on a fresh build before tagging a release. Two Windows 10/11 machines on the same LAN. Both must allow the app through the firewall on UDP 47100 + TCP 47101.

## First-time pairing
- [ ] Launch on both machines. Both show "搜索附近设备".
- [ ] Within ~5 s each side's list shows the other device.
- [ ] Click "配对" on one side. Both sides pop a dialog with the SAME 4-digit code.
- [ ] Click "确认" on both. Both windows switch to the paired chat view.

## Text messaging
- [ ] Type a message on side A, press Enter. Side B sees the bubble.
- [ ] Send Chinese / emoji / multi-line (Shift+Enter then Enter). Renders correctly.

## File transfer
- [ ] Drag a 1 MB PDF onto side A's window. Side A shows "Sending" bubble with progress; side B shows "Receiving" bubble that completes.
- [ ] File appears in side B's `%USERPROFILE%\Downloads\FileTransfer\` with the correct name.
- [ ] Drag 3 files at once. They send serially in order.
- [ ] Click "📎 文件" and select 2 files. They queue and send.
- [ ] Cancel a 100 MB file mid-transfer. Both sides show the cancelled state.

## Clipboard image
- [ ] Take a screenshot on side A (Win+Shift+S, copy to clipboard). Focus side A's window and press Ctrl+V. (Note: for v1 this is a button-driven action — actual Ctrl+V handling deferred.)

## Settings
- [ ] Open settings (⚙). Change device name, save. Restart app, name persists.
- [ ] Browse a new receive directory, save. Send a file from the other side; arrives in the new directory.
- [ ] Toggle auto-start, save. Verify HKCU\Software\Microsoft\Windows\CurrentVersion\Run has "FileTransfer" entry. Uncheck and save; entry gone.
- [ ] Note "本机指纹" displays a 64-hex value.

## Unpair + repair
- [ ] Click "取消配对" in settings. App goes back to discovery view.
- [ ] Pair again with the same peer. Works.

## Disconnect handling
- [ ] Pull side B's Wi-Fi. Side A's status switches to "离线" within ~30 s.
- [ ] Reconnect side B. Side A returns to "已连接" within a few seconds.

## Crash / shutdown
- [ ] Close window (×). App exits. No background process lingers in Task Manager.
```

- [ ] **Step 2: README**

Append to `README.md` (create if missing) at repo root:

```markdown
# FileTransfer

LAN file-transfer app for two Windows machines. .NET 8 + WPF + custom TLS protocol.

## Build and run

Prerequisites: .NET 8 SDK on Windows 10/11.

```powershell
dotnet build
dotnet test
dotnet run --project src/FileTransfer.App
```

## First-time use

1. Install on both machines. First launch creates a self-signed cert in `%APPDATA%\FileTransfer\config.json`.
2. Allow the app through Windows Firewall for **private network** when prompted (UDP 47100, TCP 47101).
3. Launch on both. Each shows the other in the discovery list.
4. Click "配对" on one side. Compare the 4-digit code on both screens; if they match, click "确认" on both.
5. You're now paired. Type messages, drag files, or click 📎 to send.

## Architecture

- `FileTransfer.Core` — headless library: protocol framing, TLS transport, file sender/receiver, Node (paired runtime), PairingService (first-time pairing).
- `FileTransfer.App` — WPF + MVVM UI (CommunityToolkit.Mvvm).
- Tests:
  - `FileTransfer.Core.Tests` — 65 xUnit tests, loopback end-to-end coverage of the protocol stack.
  - `FileTransfer.App.Tests` — xUnit tests for every ViewModel, using IDispatcher/IFilePicker/etc. fakes.

XAML is verified by manual smoke (see [docs/smoke-checklist.md](docs/smoke-checklist.md)).
```

- [ ] **Step 3: Build + run final test pass**

Run: `dotnet build` then `dotnet test`
Expected: 0 errors, all tests passing.

- [ ] **Step 4: Manual smoke (one quick pass on the dev machine)**

Run: `dotnet run --project src/FileTransfer.App`
Expected: window opens, shows "搜索附近设备" (if first run on a fresh machine). Close.

This is a sanity check — full smoke needs two machines.

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "docs(app): add smoke checklist and update README with build/run/architecture"
```

---

## After all tasks

`feature/wpf-app` is ready for the merge/PR workflow via `superpowers:finishing-a-development-branch`. The WPF UI is a complete v1 covering everything in the 2026-05-27 design's scope.

Manual smoke is the only remaining gate: do a real two-machine session per `docs/smoke-checklist.md` before merging to `main` and tagging a release.
