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
    private static ContextMenuStrip BuildMenuOnSta()
    {
        ContextMenuStrip? menu = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var container = new Container();
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
        return menu!;
    }

    [Fact]
    public void Menu_HasFourItems()
    {
        using var menu = BuildMenuOnSta();
        Assert.Equal(4, menu.Items.Count);
    }

    [Fact]
    public void Menu_FirstItem_TextIsBatteryDash()
    {
        using var menu = BuildMenuOnSta();
        Assert.Equal("Battery: --", menu.Items[0].Text);
    }

    [Fact]
    public void Menu_FirstItem_IsDisabled()
    {
        using var menu = BuildMenuOnSta();
        Assert.False(menu.Items[0].Enabled);
    }

    [Fact]
    public void Menu_ThirdItem_TextIsStartWithWindows()
    {
        using var menu = BuildMenuOnSta();
        Assert.Equal("Start with Windows", menu.Items[2].Text);
    }

    [Fact]
    public void Menu_ThirdItem_CheckedReflectsStartupManager()
    {
        using var menu = BuildMenuOnSta();
        var startupItem = (ToolStripMenuItem)menu.Items[2];
        Assert.Equal(StartupManager.IsStartupEnabled(), startupItem.Checked);
    }
}
