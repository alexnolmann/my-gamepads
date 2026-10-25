using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Win32;

sealed record DriverStatus(bool Registered, string Version)
{
    public string Description => Registered ? $"ViGEmBus установлен{(Version.Length > 0 ? $" · {Version}" : "")}" : "Для работы нужен драйвер ViGEmBus";
}
sealed record DriverInstallResult(int ExitCode)
{
    public bool Cancelled => ExitCode is 1223 or 1602;
    public bool NeedsRestart => ExitCode is 3010 or 1641;
    public bool Success => ExitCode == 0 || NeedsRestart;
}
static class DriverSetup
{
    public const string Sha256 = "89220A7865076B342892F98865F3499FB7C4CFD673159E89D352C360FD014C6A";
    public const string FileName = "ViGEmBus_1.22.0_x64_x86_arm64.exe";
    public static DriverStatus GetStatus()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\ViGEmBus");
            var sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "ViGEmBus.sys");
            return new(key != null, File.Exists(sys) ? FileVersionInfo.GetVersionInfo(sys).FileVersion ?? "" : "");
        }
        catch (Exception e) { Log.Write($"Driver detection: {e.Message}"); return new(false, ""); }
    }
    internal static void Verify(Stream data)
    {
        var actual = Convert.ToHexString(SHA256.HashData(data));
        if (!actual.Equals(Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Контрольная сумма установщика ViGEmBus не совпадает. Установка отменена.");
    }
    internal static async Task<string> ExtractVerifiedAsync()
    {
        var directory = Path.Combine(AppPaths.DataDirectory, "DriverSetup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, FileName);
        try
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MyGamepads.ViGEmBus.exe") ?? throw new FileNotFoundException("Встроенный установщик драйвера не найден.");
            await using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                await resource.CopyToAsync(file); await file.FlushAsync(); file.Position = 0; Verify(file);
            }
            return target;
        }
        catch { Cleanup(target); throw; }
    }
    internal static void Cleanup(string path)
    {
        try { File.Delete(path); Directory.Delete(Path.GetDirectoryName(path)!); } catch (Exception e) { Log.Write($"Installer cleanup: {e.Message}"); }
    }
    public static Task<DriverInstallResult> InstallAsync() => RunAsync(async path =>
    {
        // Explicit elevation applies only to the official setup, never to the bridge.
        var start = new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas", Arguments = "/norestart" };
        using var process = Process.Start(start) ?? throw new IOException("Не удалось запустить установщик.");
        await process.WaitForExitAsync(); return process.ExitCode;
    });
    internal static async Task<DriverInstallResult> RunAsync(Func<string, Task<int>> launch)
    {
        string? path = null;
        try { path = await ExtractVerifiedAsync(); return new(await launch(path)); }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { return new(1223); }
        finally { if (path != null) Cleanup(path); }
    }
}
