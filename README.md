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

## Code quality checks

Every build runs the .NET analyzers and the code-style rules in `.editorconfig`. Git hooks
([Husky.Net](https://alirezanet.github.io/Husky.Net/), configured in `.husky/task-runner.json`) enforce them:

| When | What runs |
|---|---|
| `git commit` | Formats the staged `.cs` files with `dotnet format` and re-stages them, then builds the solution in Release with warnings as errors (compile errors, analyzer findings, code style) |
| `git push` | Runs all tests |

The hooks install themselves on the first build of a fresh clone (or run `dotnet tool restore` and `dotnet husky install`).
CI runs the same checks, so nothing is skipped by committing without them.

- Format everything by hand: `dotnet format DevPilot.sln`
- Skip the hooks once (for example a work-in-progress commit): `git commit --no-verify`, `git push --no-verify`
- Turn the hooks off on a machine: set the environment variable `HUSKY=0`

## Repository layout

```
DevPilot.sln
Directory.Build.props        Settings shared by both projects (framework, version, analyzers)
dotnet-tools.json            Local .NET tools (Husky.Net for the git hooks)
global.json                  Pins the .NET SDK version
.husky/                      Git hooks: pre-commit (format, lint, build) and pre-push (tests)
installer/                   Inno Setup script for the Windows installer
.github/                     CI and release workflows, release-note categories, Dependabot
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

## Continuous integration and releases

| Workflow | Runs on | Does |
|---|---|---|
| `.github/workflows/ci.yml` | Every push to `main` and every pull request | Builds, runs the tests, then builds the x64 and ARM64 installers (attached to the run for 14 days) |
| `.github/workflows/release.yml` | Pushing a `v*.*.*` tag | Tests, builds the installers and portable zips for x64 and ARM64, and publishes a GitHub Release |

**To release a version:**

```powershell
git tag v1.2.0
git push origin v1.2.0
```

- The version comes from the tag; there is nothing to edit in the code. `v1.3.0-beta.1` publishes a **pre-release**.
- Each release contains, for x64 and ARM64: `DevPilot-<version>-win-<arch>-setup.exe` (installer) and
  `DevPilot-<version>-win-<arch>-portable.zip`, plus `SHA256SUMS.txt`. Both are self-contained (no .NET install needed).
- The release notes start with a "which file do I need" table, followed by the merged pull requests grouped by
  label (`feature`, `bug`, `ui`, `dependencies`, `maintenance`...; see `.github/release.yml`). Label pull requests
  before merging; `skip-changelog` leaves one out.
- To rebuild a release, run the **Release** workflow manually (Actions tab) with the tag.

The installer is built with [Inno Setup](https://jrsoftware.org/isinfo.php) from `installer/DevPilot.iss`. It installs
per user without admin rights (all users is optional), adds a Start menu entry and an uninstaller, and upgrades in place.
Dependabot (`.github/dependabot.yml`) opens weekly pull requests for NuGet packages and GitHub Actions.
