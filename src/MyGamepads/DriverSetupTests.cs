using System.ComponentModel;
using System.Text.Json;

static class DriverSetupTests
{
    public static async Task Run()
    {
        int assertions = 0;
        void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
        var extracted = await DriverSetup.ExtractVerifiedAsync();
        try { using var file = File.OpenRead(extracted); DriverSetup.Verify(file); Check(file.Length > 1_000_000, "Installer resource missing"); }
        finally { DriverSetup.Cleanup(extracted); }
        Check(!File.Exists(extracted), "Extracted file was not removed");
        try { using var corrupt = new MemoryStream(new byte[] { 1, 2, 3 }); DriverSetup.Verify(corrupt); throw new Exception("Corrupt payload accepted"); }
        catch (InvalidDataException) { assertions++; }
        foreach (int code in new[] { 0, 3010, 1641, 1602, 1603 })
        {
            string? actualPath = null;
            var result = await DriverSetup.RunAsync(path => { actualPath = path; Check(File.Exists(path), "Missing installer at launch"); return Task.FromResult(code); });
            Check(result.Success == (code is 0 or 3010 or 1641), "Success classification");
            Check(result.NeedsRestart == (code is 3010 or 1641), "Restart classification");
            Check(result.Cancelled == (code == 1602), "Cancellation classification");
            Check(!File.Exists(actualPath), "Installer cleanup after launch");
        }
        string? cancelledPath = null;
        var cancelled = await DriverSetup.RunAsync(path => { cancelledPath = path; throw new Win32Exception(1223); });
        Check(cancelled.Cancelled, "UAC cancellation was not handled"); Check(!File.Exists(cancelledPath), "UAC cancellation cleanup");
        string? failedPath = null;
        try { await DriverSetup.RunAsync(path => { failedPath = path; throw new IOException("simulated launch failure"); }); throw new Exception("Launch exception swallowed"); }
        catch (IOException) { Check(!File.Exists(failedPath), "Launch failure cleanup"); }
        File.WriteAllText(AppPaths.FilePath("setup-tests.json"), JsonSerializer.Serialize(new { Result = "PASS", Assertions = assertions, SystemInstallerExecuted = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
