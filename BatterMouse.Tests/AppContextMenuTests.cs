using System.ComponentModel;
using System.Threading;
using System.Windows.Forms;
using BatterMouse;

namespace BatterMouse.Tests;

/// <summary>
/// Unit tests for AppContext menu structure.
/// BuildMenuInternal is exposed as internal via InternalsVisibleTo.
/// Tests run on STA thread (required for WinForms ContextMenuStrip construction).
/// </summary>
public class AppContextMenuTests
{
    /// <summary>
    /// Builds the menu on an STA thread (required for WinForms) and returns both the menu
    /// and its container. Caller must dispose both when done.
    /// </summary>
    private static (ContextMenuStrip menu, Container container) BuildMenuOnSta()
    {
        ContextMenuStrip? menu = null;
        Container? container = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                container = new Container();
                menu = AppContext.BuildMenuInternal(container);
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null) throw new Exception("STA thread threw", ex);
        return (menu!, container!);
    }

    [Fact]
    public void Menu_HasFourItems()
    {
        var (menu, container) = BuildMenuOnSta();
        using (container) using (menu)
            Assert.Equal(4, menu.Items.Count);
    }

    [Fact]
    public void Menu_FirstItem_TextIsBatteryDash()
    {
        var (menu, container) = BuildMenuOnSta();
        using (container) using (menu)
            Assert.Equal("Battery: --", menu.Items[0].Text);
    }

    [Fact]
    public void Menu_FirstItem_IsDisabled()
    {
        var (menu, container) = BuildMenuOnSta();
        using (container) using (menu)
            Assert.False(menu.Items[0].Enabled);
    }

    [Fact]
    public void Menu_ThirdItem_TextIsStartWithWindows()
    {
        var (menu, container) = BuildMenuOnSta();
        using (container) using (menu)
            Assert.Equal("Start with Windows", menu.Items[2].Text);
    }

    [Fact]
    public void Menu_ThirdItem_TextReflectsStartupManager()
    {
        var (menu, container) = BuildMenuOnSta();
        using (container)
        {
            using (menu)
            {
                var startupItem = (ToolStripMenuItem)menu.Items[2];
                bool expected = StartupManager.IsStartupEnabled();
                Assert.Equal(expected, startupItem.Text.StartsWith("✓"));
            }
        }
    }
}
