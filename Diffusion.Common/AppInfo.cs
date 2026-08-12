using System;
using System.IO;

namespace Diffusion.Common;

public static class AppInfo
{
    private const string AppName = "DiffusionToolkit";
    public static string AppDir { get; }
    public static SemanticVersion Version => SemanticVersionHelper.GetLocalVersion();
    public static string AppDataPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DiffusionToolkit");

    public static string DatabasePath { get; }

    public static string SettingsPath { get; }

    /// <summary>
    /// Settings that only exist in this build. Kept in a separate file so that the original
    /// Diffusion Toolkit, which rewrites config.json wholesale when it saves, cannot drop them.
    /// </summary>
    public static string ExtendedSettingsPath { get; }

    public static bool IsPortable { get; }

    static AppInfo()
    {
        AppDir = AppDomain.CurrentDomain.BaseDirectory;

        if (AppDir.EndsWith("\\"))
        {
            AppDir = AppDir.Substring(0, AppDir.Length - 1);
        }

        DatabasePath = Path.Combine(AppInfo.AppDir, "diffusion-toolkit.db");

        IsPortable = true;

        SettingsPath = Path.Combine(AppInfo.AppDir, "config.json");

        if (!File.Exists(SettingsPath))
        {
            IsPortable = false;
            SettingsPath = Path.Combine(AppInfo.AppDataPath, "config.json");
            DatabasePath = Path.Combine(AppInfo.AppDataPath, "diffusion-toolkit.db");
        }

        ExtendedSettingsPath = Path.Combine(Path.GetDirectoryName(SettingsPath)!, "config.extended.json");
    }


}