namespace ZapretGui.Core;

public static class AppPaths
{
    public static string AppDir => AppContext.BaseDirectory;

    public static string DataDir { get; } = ResolveDataDir();

    public static string SettingsFile => Path.Combine(DataDir, "zapret-gui.json");

    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppDir, "ZapretGUI");

    public static string DefaultZapretDir
    {
        get
        {
            var nearExe = Path.Combine(AppDir, "zapret");
            if (OperatingSystem.IsWindows()) return nearExe;
            var inData = Path.Combine(DataDir, "zapret");
            return Directory.Exists(nearExe) && !Directory.Exists(inData) ? nearExe : inData;
        }
    }

    private static string ResolveDataDir()
    {
        if (OperatingSystem.IsWindows()) return AppDir;

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var baseDir = string.IsNullOrEmpty(xdg)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : xdg;
        var dir = Path.Combine(baseDir, "zapret-gui");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
