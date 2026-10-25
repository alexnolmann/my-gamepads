internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        int output = Array.IndexOf(args, "--output-dir");
        if (output >= 0 && output + 1 < args.Length) AppPaths.OutputOverride = Path.GetFullPath(args[output + 1]);
        if (args.Contains("--setup-test")) { DriverSetupTests.Run().GetAwaiter().GetResult(); return; }
        if (args.Contains("--check-driver"))
        {
            try { using var client = new Nefarius.ViGEm.Client.ViGEmClient(); File.WriteAllText(AppPaths.FilePath("driver-check.txt"), $"PASS: {DriverSetup.GetStatus().Description}"); }
            catch (Exception e) { File.WriteAllText(AppPaths.FilePath("driver-check.txt"), e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--layout-check")) { ApplicationConfiguration.Initialize(); LayoutCheck.Run(); return; }
        if (args.Contains("--probe")) { Diagnostics.Probe().GetAwaiter().GetResult(); return; }
        if (args.Contains("--diagnose")) { Diagnostics.Run().GetAwaiter().GetResult(); return; }
        if (args.Contains("--self-test")) { Diagnostics.Test(); return; }
        using var single = new Mutex(true, "Local\\MyPads-054C-05C4-09CC", out bool first);
        if (!first) { MessageBox.Show("Приложение уже запущено. Проверьте значок геймпада в трее.", "Мои геймпады"); return; }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
