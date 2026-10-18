using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;

namespace FanCurve
{
    // Arranque con Windows mediante una tarea programada con privilegios elevados
    // (el programa necesita admin y una entrada en "Run" dispararia el aviso de UAC).
    public static class Autostart
    {
        const string TaskName = "FanCurve";

        public static bool IsEnabled()
        {
            return Run("/Query /TN " + TaskName) == 0;
        }

        public static bool Enable()
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string xml =
@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers>
    <LogonTrigger><Enabled>true</Enabled><Delay>PT10S</Delay></LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Enabled>true</Enabled>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>" + SecurityElement.Escape(exe) + @"</Command>
      <Arguments>--minimized</Arguments>
    </Exec>
  </Actions>
</Task>";
            string tmp = Path.Combine(Path.GetTempPath(), "fancurve-task.xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try { return Run("/Create /TN " + TaskName + " /XML \"" + tmp + "\" /F") == 0; }
            finally { try { File.Delete(tmp); } catch { } }
        }

        public static bool Disable()
        {
            return Run("/Delete /TN " + TaskName + " /F") == 0;
        }

        static int Run(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }
    }
}
