using Microsoft.Win32;
using Xunit;

namespace BatterMouse.Tests;

// Tests for StartupManager registry operations — covers STARTUP-01
// These are RED stubs: StartupManager does not exist yet (plan 02-03).
// NOTE: These tests write to HKCU — they are safe (no elevation), but do touch the registry.
// They clean up after themselves via the fixture teardown.
public class StartupManagerTests : IDisposable
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string TestAppName = "BatterMouse";

    // Teardown: always remove the registry key after each test
    public void Dispose()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(TestAppName, throwOnMissingValue: false);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SetStartup_Enable_WritesRegistryKey()
    {
        StartupManager.SetStartup(true);

        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        Assert.NotNull(key?.GetValue(TestAppName));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void SetStartup_Disable_RemovesRegistryKey()
    {
        // Arrange: write key first
        StartupManager.SetStartup(true);

        // Act: disable
        StartupManager.SetStartup(false);

        // Assert: key is gone
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        Assert.Null(key?.GetValue(TestAppName));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsStartupEnabled_ReturnsFalse_WhenKeyAbsent()
    {
        // Ensure key is absent
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(TestAppName, throwOnMissingValue: false);

        Assert.False(StartupManager.IsStartupEnabled());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void IsStartupEnabled_ReturnsTrue_WhenKeyPresent()
    {
        StartupManager.SetStartup(true);
        Assert.True(StartupManager.IsStartupEnabled());
    }
}
