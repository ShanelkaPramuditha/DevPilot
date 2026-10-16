namespace DevPilot.Services.Updates;

/// <summary>How the running copy of DevPilot was installed.</summary>
public enum InstallKind
{
    /// <summary>Installed by the setup for the current user (no admin rights needed to update).</summary>
    CurrentUser,

    /// <summary>Installed by the setup for all users (updating asks for admin rights).</summary>
    AllUsers,

    /// <summary>Run from the portable zip or a development build; the setup cannot update it in place.</summary>
    Portable
}
