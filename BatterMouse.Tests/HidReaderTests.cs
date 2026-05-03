using Xunit;

namespace BatterMouse.Tests;

// Tests for HidReader.ParseBattery — covers HID-01
// These are RED stubs: they reference HidReader which does not exist yet (plan 02-02).
// They WILL fail to compile until 02-02 creates HidReader.cs.
public class HidReaderTests
{
    // Helper: build a minimal valid 32-byte battery report with the correct report ID.
    private static byte[] MakeReport(byte batteryPct)
    {
        var report = new byte[32];
        report[0] = HidReader.BatteryReportId; // 0x54
        report[5] = batteryPct;
        return report;
    }

    // Helper: build a 32-byte report where r[1] encodes the keyboard battery.
    private static byte[] MakeKeyboardReport(byte r1)
    {
        var report = new byte[32];
        report[0] = HidReader.BatteryReportId; // 0x54
        report[1] = r1;
        return report;
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseBattery_ReturnsValueAtOffset5()
    {
        // report[5] = 70 (0x46) — typical battery reading
        int? result = HidReader.ParseBattery(MakeReport(0x46));
        Assert.Equal(70, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseBattery_ReturnsBoundaryValue_20Percent()
    {
        int? result = HidReader.ParseBattery(MakeReport(20));
        Assert.Equal(20, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseBattery_ReturnsNull_WhenReportTooShort()
    {
        // A report shorter than 6 bytes cannot contain offset 5
        var report = new byte[5]; // length 5 — offset 5 does not exist
        int? result = HidReader.ParseBattery(report);
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseBattery_ReturnsNull_ForEmptyReport()
    {
        var report = Array.Empty<byte>();
        int? result = HidReader.ParseBattery(report);
        Assert.Null(result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseBattery_ReturnsNull_WhenReportIdDoesNotMatch()
    {
        // Reproduces the Keychron Launcher bug: dongle emits an extra report type whose
        // byte 5 is 0x02 or 0x04, not the real battery percentage.
        var report = new byte[32];
        report[0] = 0x01; // wrong report ID — not the battery report
        report[5] = 0x02; // would be misread as "2%" without the ID guard
        int? result = HidReader.ParseBattery(report);
        Assert.Null(result);
    }

    // --- Keyboard 54-E4 format tests ---

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseBattery_KeyboardReport_E4_Returns100()
    {
        // 0xE4 & 0x7F = 0x64 = 100
        int? result = HidReader.ParseBattery(MakeKeyboardReport(0xE4));
        Assert.Equal(100, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseBattery_KeyboardReport_Returns50_For0xB2()
    {
        // 0xB2 & 0x7F = 0x32 = 50
        int? result = HidReader.ParseBattery(MakeKeyboardReport(0xB2));
        Assert.Equal(50, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseBattery_MouseReport_E2_ReadsOffset5_NotR1()
    {
        // Regression: 54-E2 must read battery from r[5], not from r[1] & 0x7F.
        // r[1]=0xE2, r[5]=75 → expected 75 (not 0xE2 & 0x7F = 98)
        var report = MakeKeyboardReport(0xE2); // sets r[1]=0xE2
        report[5] = 75;
        int? result = HidReader.ParseBattery(report);
        Assert.Equal(75, result);
    }
}
