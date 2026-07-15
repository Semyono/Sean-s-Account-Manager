using System.IO;
using System.Reflection;
using System.Windows;
using Seans_Account_Manager.Services;

namespace Seans_Account_Manager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ExtractEmbeddedNativeDlls();
        MultiRobloxService.Enable();
    }

    private void ExtractEmbeddedNativeDlls()
    {
        try
        {
            string exeDir = AppContext.BaseDirectory;
            var asm = Assembly.GetExecutingAssembly();

            foreach (var resourceName in asm.GetManifestResourceNames())
            {
                if (!resourceName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;
                string fileName = resourceName.Substring(resourceName.LastIndexOf('.', resourceName.Length - 5) + 1);
                string destPath = Path.Combine(exeDir, fileName);

                if (File.Exists(destPath)) continue; 

                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var fileStream = File.Create(destPath);
                stream.CopyTo(fileStream);
            }
        }
        catch
        {

        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        RobloxProcessService.KillAll();
        MultiRobloxService.Disable();
        base.OnExit(e);
    }
}