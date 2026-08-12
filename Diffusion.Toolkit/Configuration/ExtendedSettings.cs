using System;
using System.Collections.Generic;

namespace Diffusion.Toolkit.Configuration;

/// <summary>
/// Settings and per-library data that only exist in this build.
///
/// These deliberately live outside config.json and outside the database. The original
/// Diffusion Toolkit ignores unknown keys when it reads config.json, but it re-serializes the
/// whole settings object when it saves, which would silently drop anything it does not know
/// about. Keeping our state in a sidecar file means both applications can share the same
/// library without either one damaging the other's data.
/// </summary>
public class ExtendedSettings : SettingsContainer
{
    private Dictionary<string, string> _folderDisplayNames = NewNameDictionary(null);
    private VideoMetadataSettings _videoMetadata = new VideoMetadataSettings();

    public ExtendedSettings()
    {
        MuteSidePanelVideo = true;
        InfoOverlayOnRightEdge = false;
        InfoOverlayEdgeWidth = 8;
        VideoSectionState = AccordionState.Expanded;
        VideoMetadata = new VideoMetadataSettings();
    }

    /// <summary>
    /// Display name overrides for root folders, keyed by folder path. Purely cosmetic - the
    /// folder on disk and the Folder row in the database are never touched.
    /// </summary>
    /// <remarks>
    /// The setter rebuilds the dictionary because System.Text.Json hands us a fresh instance
    /// built with the default comparer, and paths need to compare case-insensitively.
    /// </remarks>
    public Dictionary<string, string> FolderDisplayNames
    {
        get => _folderDisplayNames;
        set => _folderDisplayNames = NewNameDictionary(value);
    }

    /// <summary>
    /// Mute video played in the docked preview pane. The popped out viewer is unaffected.
    /// </summary>
    public bool MuteSidePanelVideo
    {
        get;
        set => UpdateValue(ref field, value);
    }

    /// <summary>
    /// Reveal the info overlay when the cursor reaches the right edge in the full screen viewer.
    /// </summary>
    public bool InfoOverlayOnRightEdge
    {
        get;
        set => UpdateValue(ref field, value);
    }

    /// <summary>
    /// Width in pixels of the hot zone at the right edge that reveals the info overlay.
    /// </summary>
    public int InfoOverlayEdgeWidth
    {
        get;
        set => UpdateValue(ref field, value);
    }

    public AccordionState VideoSectionState
    {
        get;
        set => UpdateValue(ref field, value);
    }

    /// <summary>
    /// The setter re-subscribes because System.Text.Json replaces the instance built in the
    /// constructor, which would otherwise leave changes to the nested section unsaved.
    /// </summary>
    public VideoMetadataSettings VideoMetadata
    {
        get => _videoMetadata;
        set
        {
            _videoMetadata = value ?? new VideoMetadataSettings();
            _videoMetadata.SettingChanged += (sender, args) => RaiseSettingChanged(nameof(VideoMetadata));
        }
    }

    /// <summary>
    /// Returns the display name override for a root folder path, or null if there isn't one.
    /// </summary>
    public string? GetFolderDisplayName(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        return _folderDisplayNames.TryGetValue(path, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;
    }

    /// <summary>
    /// Sets or, when <paramref name="name"/> is empty, clears a root folder's display name.
    /// </summary>
    public void SetFolderDisplayName(string path, string? name)
    {
        if (string.IsNullOrEmpty(path)) return;

        if (string.IsNullOrWhiteSpace(name))
        {
            if (_folderDisplayNames.Remove(path))
            {
                RaiseSettingChanged(nameof(FolderDisplayNames));
            }

            return;
        }

        var trimmed = name.Trim();

        if (_folderDisplayNames.TryGetValue(path, out var existing) && existing == trimmed) return;

        _folderDisplayNames[path] = trimmed;

        RaiseSettingChanged(nameof(FolderDisplayNames));
    }

    /// <summary>
    /// Follows a folder whose path has actually changed on disk so its display name isn't orphaned.
    /// </summary>
    public void MoveFolderDisplayName(string oldPath, string newPath)
    {
        if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath)) return;
        if (!_folderDisplayNames.Remove(oldPath, out var name)) return;

        _folderDisplayNames[newPath] = name;

        RaiseSettingChanged(nameof(FolderDisplayNames));
    }

    private static Dictionary<string, string> NewNameDictionary(Dictionary<string, string>? source)
    {
        return source == null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
    }
}
