using System.Threading;
using System.Windows.Forms;

namespace BatterMouse;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        bool createdNew;
        using var mutex = new Mutex(true, "BatterMouse_SingleInstance", out createdNew);
        if (!createdNew) return;  // second instance — exit silently

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new AppContext());
    }
}
