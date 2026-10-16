<img src="Assets/DevPilot.png" alt="DevPilot logo" width="96" align="right">

# DevPilot

DevPilot is a Windows desktop app (WPF, .NET 10) for managing WSL: install WSL and
Linux distributions, start/stop/configure them, mount Windows folders, and watch
resource usage.

## Build, run and test

```powershell
dotnet build DevPilot.sln
dotnet run
dotnet watch                   # run and restart on changes
dotnet test DevPilot.sln
```

In VS Code the same commands are available as tasks (**Terminal → Run Task**: `watch`, `build`, `test`).

## Repository layout

```
DevPilot.sln
Directory.Build.props        Settings shared by both projects (framework, version, authors)
DevPilot.csproj              The application (its folders are below)
App.xaml(.cs)                Startup: applies the theme, opens Views/MainWindow.xaml
Assets/                      DevPilot.ico (app and window icon, 16–256 px) and DevPilot.png (512 px logo)
Core/                        Small building blocks with no WSL knowledge (Format, IniDocument, ObservableObject)
Models/                      Data shown in the UI: distributions, catalog entries, usage, mounts, settings
Services/
  Wsl/                       Everything that talks to wsl.exe or reads WSL data
  Platform/                  Windows-side helpers (system memory, VS Code)
  AppPreferences.cs          Preferences saved in %LocalAppData%\DevPilot
Theming/                     Light/dark theme and the app's colour palette
Navigation/                  Sidebar: PageInfo attached properties and NavItem
Controls/                    Reusable controls (StatTile, HistoryChart)
Views/
  MainWindow.xaml            Shell (sidebar + page area) and the pages
  MainWindow.*.cs            One partial file per area (Catalog, DistroActions, Usage, Sidebar, Shell, WslLifecycle)
  Dialogs/                   Settings, Folder Mounts, Add Mount
tests/                       xUnit tests (DevPilot.Tests.csproj); folders mirror the app
```

## Conventions

- **Namespaces match folders** (`Services/Wsl` is `DevPilot.Services.Wsl`). This is enforced by `.editorconfig`.
- **One top-level type per file**, named after the type.
- **Dependencies point downwards:** Views → Controls/Navigation/Theming → Services → Models → Core.
  Services and Models never reference WPF windows.
- **Colours come from the theme:** use `{DynamicResource Brush.*}` (see `Theming/ThemeManager.cs`), never hard-coded colours, so light and dark mode both work.
- **New logic gets tests** in `tests/`, in the folder matching the code under test.

## Adding a page

Pages are `TabItem`s inside `DistroTabs` in `Views/MainWindow.xaml`; the sidebar is built from them.

```xml
<TabItem x:Name="ToolsTab" Header="Dev Tools"
         nav:PageInfo.Glyph="&#xE90F;"
         nav:PageInfo.Group="Developer tools"
         nav:PageInfo.Subtitle="Install and update the tools in your distributions.">
    <!-- page content -->
</TabItem>
```

Put the page's code in a new partial file such as `Views/MainWindow.Tools.cs`, and add it to the
`DependentUpon` list in `DevPilot.csproj` so it nests under `MainWindow.xaml`. Use
`PageInfo.SetBadge(ToolsTab, "3")` to show a count in the sidebar. Glyphs are Segoe Fluent Icons code points.
