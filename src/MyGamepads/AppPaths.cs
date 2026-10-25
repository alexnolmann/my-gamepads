static class AppPaths
{
    internal static string? OutputOverride { get; set; }
    public static string DataDirectory => OutputOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyGamepads");
    public static string FilePath(string name) { Directory.CreateDirectory(DataDirectory); return Path.Combine(DataDirectory, name); }
}
