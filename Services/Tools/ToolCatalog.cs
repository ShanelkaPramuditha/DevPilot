using DevPilot.Models;

namespace DevPilot.Services.Tools;

/// <summary>
/// The developer tools DevPilot offers. Windows packages are winget ids; WSL packages are
/// listed per package manager (apt-get, dnf, pacman, zypper). Leave a package manager out
/// when its distributions do not ship the tool.
/// </summary>
public static class ToolCatalog
{
    public static IReadOnlyList<DevTool> Tools { get; } =
    [
        new("git", "Git", "Version control.", "https://git-scm.com",
            new("Git.Git", @"^Git\.Git$"),
            new("git", "--version", Same("git"))),

        new("gh", "GitHub CLI", "GitHub from the terminal: pull requests, issues, releases.", "https://cli.github.com",
            new("GitHub.cli", @"^GitHub\.cli$"),
            new("gh", "--version", Packages(apt: "gh", dnf: "gh", pacman: "github-cli", zypper: "gh"))),

        new("docker", "Docker", "Containers. Docker Desktop on Windows, Docker Engine inside a distribution.", "https://www.docker.com",
            new("Docker.DockerDesktop", @"^Docker\.DockerDesktop$"),
            new("docker", "--version",
                Packages(apt: "docker.io", dnf: "moby-engine", pacman: "docker", zypper: "docker"),
                OptionalPackages: Packages(apt: "docker-compose-v2", dnf: "docker-compose", pacman: "docker-compose", zypper: "docker-compose"),
                // Let the default user run docker without sudo, and start the service.
                PostInstall: """
                    usermod -aG docker "$1" 2>/dev/null || true
                    if [ -d /run/systemd/system ]; then systemctl enable --now docker; fi
                    """),
            WslNote: "Installs Docker Engine in this distribution. If you use Docker Desktop, turn on its WSL integration instead."),

        new("node", "Node.js", "JavaScript runtime, with npm.", "https://nodejs.org",
            new("OpenJS.NodeJS.LTS", @"^OpenJS\.NodeJS(\.LTS)?$"),
            new("node", "--version", Packages(apt: "nodejs npm", dnf: "nodejs npm", pacman: "nodejs npm", zypper: "nodejs-default npm-default"))),

        new("python", "Python", "Python 3, with pip.", "https://www.python.org",
            new("Python.Python.3.13", @"^Python\.Python\.3\.\d+$"),
            new("python3", "--version", Packages(apt: "python3 python3-pip python3-venv", dnf: "python3 python3-pip", pacman: "python python-pip", zypper: "python3 python3-pip"))),

        new("dotnet", ".NET SDK", "Build .NET apps (C#, F#).", "https://dotnet.microsoft.com",
            new("Microsoft.DotNet.SDK.10", @"^Microsoft\.DotNet\.SDK\.\d+$"),
            new("dotnet", "--version", Packages(apt: "dotnet-sdk-10.0", dnf: "dotnet-sdk-10.0", pacman: "dotnet-sdk"))),

        new("go", "Go", "The Go programming language.", "https://go.dev",
            new("GoLang.Go", @"^GoLang\.Go$"),
            new("go", "version", Packages(apt: "golang-go", dnf: "golang", pacman: "go", zypper: "go"))),

        new("rust", "Rust", "Rust compiler and Cargo.", "https://www.rust-lang.org",
            new("Rustlang.Rustup", @"^Rustlang\.Rust(up|\..+)$"),
            new("rustc", "--version", Packages(apt: "rustc cargo", dnf: "rust cargo", pacman: "rust", zypper: "rust cargo"))),

        new("java", "Java (OpenJDK 21)", "Java Development Kit.", "https://openjdk.org",
            new("Microsoft.OpenJDK.21", @"^(Microsoft\.OpenJDK|EclipseAdoptium\.Temurin)\.\d+(\.JDK)?$"),
            new("java", "-version", Packages(apt: "openjdk-21-jdk", dnf: "java-21-openjdk-devel", pacman: "jdk21-openjdk", zypper: "java-21-openjdk-devel"))),

        new("build", "Build tools", "C/C++ compiler (gcc), make and related tools.", "https://gcc.gnu.org",
            null,
            new("gcc", "--version", Packages(apt: "build-essential", dnf: "gcc gcc-c++ make", pacman: "base-devel", zypper: "gcc gcc-c++ make"))),

        new("vscode", "Visual Studio Code", "Code editor. Opens WSL folders with its WSL extension.", "https://code.visualstudio.com",
            new("Microsoft.VisualStudioCode", @"^Microsoft\.VisualStudioCode$"),
            null,
            WslNote: "Install VS Code on Windows; it connects to WSL distributions by itself."),

        new("terminal", "Windows Terminal", "Tabs for PowerShell, Command Prompt and every WSL distribution.", "https://aka.ms/terminal",
            new("Microsoft.WindowsTerminal", @"^Microsoft\.WindowsTerminal(\.Preview)?$"),
            null,
            WslNote: "A Windows app; it opens WSL distributions as tabs."),

        new("pwsh", "PowerShell 7", "The current, cross-platform PowerShell.", "https://aka.ms/powershell",
            new("Microsoft.PowerShell", @"^Microsoft\.PowerShell(\.Preview)?$"),
            null,
            WslNote: "Offered for Windows only."),
    ];

    /// <summary>The same package name for every package manager.</summary>
    private static Dictionary<string, string> Same(string package) =>
        Packages(apt: package, dnf: package, pacman: package, zypper: package);

    private static Dictionary<string, string> Packages(string? apt = null, string? dnf = null, string? pacman = null, string? zypper = null)
    {
        var packages = new Dictionary<string, string>();
        void Add(string manager, string? names)
        {
            if (names is not null)
            {
                packages[manager] = names;
            }
        }

        Add("apt-get", apt);
        Add("dnf", dnf);
        Add("pacman", pacman);
        Add("zypper", zypper);
        return packages;
    }
}
