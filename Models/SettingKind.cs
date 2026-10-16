namespace DevPilot.Models;

public enum SettingKind
{
    /// <summary>Default / On / Off.</summary>
    Bool,

    /// <summary>Default or one of a fixed list.</summary>
    Choice,

    /// <summary>Free text; empty means the default.</summary>
    Text,

    /// <summary>A size such as 4GB; empty means the default.</summary>
    Size,

    /// <summary>A whole number; empty means the default.</summary>
    Number
}
