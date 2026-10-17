using System.Runtime.InteropServices;

namespace DevPilot.Services.Platform;

/// <summary>
/// Stops Windows from going to sleep on its idle timer, for as long as it is turned on.
/// The screen can still turn off, and closing the lid or choosing Sleep still sleeps.
/// Windows drops the request when DevPilot exits.
/// </summary>
public static class SleepBlocker
{
    private const uint Continuous = 0x80000000;
    private const uint SystemRequired = 0x00000001;

    public static bool IsActive { get; private set; }

    /// <summary>Turns the request on or off. Call it from the UI thread: the request belongs to the calling thread.</summary>
    public static void KeepAwake(bool on)
    {
        if (on == IsActive)
        {
            return;
        }

        // Returns the previous state, or 0 when the call failed.
        if (SetThreadExecutionState(on ? Continuous | SystemRequired : Continuous) != 0)
        {
            IsActive = on;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint flags);
}
