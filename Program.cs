using System;
using System.IO;
using Avalonia;

namespace ZoteroWorkflowApp;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            var crashPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "zotero-workflow-crash.log");
            File.WriteAllText(crashPath, ex.ToString());
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
