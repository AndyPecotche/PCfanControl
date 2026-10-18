using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace FanCurve
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "Global\\FanCurve.SingleInstance", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(args.Contains("--minimized")));
            }
        }
    }
}
