using BatterMouse;

namespace BatterMouse.Tests;

/// <summary>
/// Unit tests confirming tray.ico is embedded in the BatterMouse assembly.
/// No STA thread required — stream operations are thread-agnostic.
/// </summary>
public class IconResourceTests
{
    [Fact]
    public void TrayIco_IsEmbeddedResource()
    {
        using var stream = typeof(AppContext).Assembly
            .GetManifestResourceStream("BatterMouse.Resources.tray.ico");
        Assert.NotNull(stream);
    }

    [Fact]
    public void TrayIco_StreamLengthIsPositive()
    {
        using var stream = typeof(AppContext).Assembly
            .GetManifestResourceStream("BatterMouse.Resources.tray.ico");
        Assert.NotNull(stream);
        Assert.True(stream!.Length > 0);
    }
}
