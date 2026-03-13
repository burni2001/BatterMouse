using Xunit;

namespace BatterMouse.Tests;

// Tests for HidReader.ParseBattery — covers HID-01
// These are RED stubs: they reference HidReader which does not exist yet (plan 02-02).
// They WILL fail to compile until 02-02 creates HidReader.cs.
public class HidReaderTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ParseBattery_ReturnsValueAtOffset5()
    {
        // report[5] = 70 (0x46) — typical battery reading
        var report = new byte[32];
        report[5] = 0x46; // 70%
        int? result = HidReader.ParseBattery(report);
        Assert.Equal(70, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ParseBattery_ReturnsBoundaryValue_20Percent()
    {
        var report = new byte[32];
        report[5] = 20;
        int? result = HidReader.ParseBattery(report);
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
}
