using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace LakeShoreControl
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--self-test") return SelfTests.Run(args[1]);
            UiText.LoadPreference();
            if (args.Length >= 3 && args[0] == "--probe")
            {
                try
                {
                    using (SerialInstrument device = new SerialInstrument(args[1]))
                    {
                        Snapshot data = device.Read(1, 1);
                        File.WriteAllText(args[2], new JavaScriptSerializer().Serialize(new { identity = device.Identity, snapshot = data, setpointK = data.SetpointK, readOnly = true }));
                    }
                    return 0;
                }
                catch (Exception e) { File.WriteAllText(args[2], "ERROR: " + e.Message); return 1; }
            }
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\LakeShoreControl336.GUI", out created))
            {
                if (!created) { UiMessageBox.Show("温控程序已经运行。请使用已打开的窗口，避免同时占用串口。", "Lake Shore 336"); return 0; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string autoPort = null, recipe = null, pausedPath = null; bool demo = false;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--connect" && i + 1 < args.Length) autoPort = args[++i];
                    else if (args[i] == "--recipe" && i + 1 < args.Length) recipe = args[++i];
                    else if (args[i] == "--restore-paused" && i + 1 < args.Length) pausedPath = args[++i];
                    else if (args[i] == "--demo") demo = true;
                }
                string defaultRecipe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "recipes", "默认温度程序.json");
                if (recipe == null && File.Exists(defaultRecipe)) recipe = defaultRecipe;
                Application.Run(new MainForm(autoPort, demo, recipe, pausedPath));
                mutex.ReleaseMutex();
            }
            return 0;
        }
    }
}
