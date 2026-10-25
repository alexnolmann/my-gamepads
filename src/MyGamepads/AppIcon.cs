using System.Reflection;

static class AppIcon
{
    // Shared for the process lifetime: window, taskbar, tray and license dialog.
    public static readonly Icon Value = Load();

    static Icon Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MyGamepads.App.ico")
            ?? throw new FileNotFoundException("Application icon resource is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }
}
