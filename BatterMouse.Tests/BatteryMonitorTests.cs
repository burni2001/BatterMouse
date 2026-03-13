using Xunit;

namespace BatterMouse.Tests;

// Tests for BatteryMonitor threshold logic — covers NOTF-01
// These are RED stubs: BatteryMonitor does not exist yet (plan 02-02).
public class BatteryMonitorTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void OnBatteryLevel_FiresNotification_WhenLevelIs20()
    {
        int notifiedLevel = -1;
        var monitor = new BatteryMonitor(level => { notifiedLevel = level; });
        monitor.OnBatteryLevel(20);
        Assert.Equal(20, notifiedLevel);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OnBatteryLevel_FiresNotification_WhenLevelBelow20()
    {
        int notifiedLevel = -1;
        var monitor = new BatteryMonitor(level => { notifiedLevel = level; });
        monitor.OnBatteryLevel(15);
        Assert.Equal(15, notifiedLevel);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OnBatteryLevel_DoesNotRefire_WhileStillBelowThreshold()
    {
        int callCount = 0;
        var monitor = new BatteryMonitor(_ => { callCount++; });
        monitor.OnBatteryLevel(20); // first call: fires
        monitor.OnBatteryLevel(18); // still below: must NOT fire again
        monitor.OnBatteryLevel(10); // still below: must NOT fire again
        Assert.Equal(1, callCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OnBatteryLevel_RefiresAfterRecovery()
    {
        int callCount = 0;
        var monitor = new BatteryMonitor(_ => { callCount++; });
        monitor.OnBatteryLevel(20); // fires
        monitor.OnBatteryLevel(50); // recovery above threshold — resets state
        monitor.OnBatteryLevel(20); // fires again
        Assert.Equal(2, callCount);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OnBatteryLevel_DoesNotFire_WhenAboveThreshold()
    {
        int callCount = 0;
        var monitor = new BatteryMonitor(_ => { callCount++; });
        monitor.OnBatteryLevel(50);
        monitor.OnBatteryLevel(80);
        Assert.Equal(0, callCount);
    }
}
