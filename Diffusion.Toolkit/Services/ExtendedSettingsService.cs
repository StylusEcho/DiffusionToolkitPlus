using System;
using Diffusion.Common;
using Diffusion.Toolkit.Configuration;

namespace Diffusion.Toolkit.Services;

/// <summary>
/// Owns the sidecar settings file. Loading is tolerant of a missing or unreadable file so that a
/// library which has only ever been opened by the original Diffusion Toolkit still works.
/// </summary>
public class ExtendedSettingsService
{
    private readonly Configuration<ExtendedSettings> _configuration;
    private readonly Action _debounceSave;

    public ExtendedSettings Settings { get; private set; }

    public ExtendedSettingsService()
    {
        _configuration = new Configuration<ExtendedSettings>(AppInfo.ExtendedSettingsPath, AppInfo.IsPortable);

        Settings = Load();

        _debounceSave = Utility.Debounce(Save, 500);

        Settings.SettingChanged += (sender, args) => _debounceSave();
    }

    private ExtendedSettings Load()
    {
        if (!_configuration.TryLoad(out var settings) || settings == null)
        {
            return new ExtendedSettings();
        }

        settings.SetPristine();

        return settings;
    }

    /// <summary>
    /// Writes the sidecar immediately. Called on shutdown, and on a debounce as settings change.
    /// </summary>
    public void Save()
    {
        try
        {
            _configuration.Save(Settings);
            Settings.SetPristine();
        }
        catch (Exception e)
        {
            // Never let a settings write take the application down - these are all conveniences,
            // and the database and config.json are what actually matter.
            Logger.Log($"Failed to save extended settings: {e.Message}");
        }
    }
}
