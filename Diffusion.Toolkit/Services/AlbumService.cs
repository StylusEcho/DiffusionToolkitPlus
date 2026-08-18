using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Diffusion.Database.Models;
using Diffusion.Toolkit.Localization;
using Diffusion.Toolkit.Models;

namespace Diffusion.Toolkit.Services;

public class AlbumService
{
    private string GetLocalizedText(string key)
    {
        return (string)JsonLocalizationProvider.Instance.GetLocalizedObject(key, null, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reloads the album list in the navigation pane. Set by the main window.
    /// </summary>
    public Func<Task>? ReloadAlbums;

    private HashSet<int> _quickAlbumImageIds = new();

    /// <summary>
    /// Re-reads which images are in the quick album so thumbnails can show their badge without a
    /// query per thumbnail. Cheap - the quick album is small by nature.
    /// </summary>
    public void RefreshQuickAlbum()
    {
        try
        {
            var name = ServiceLocator.ExtendedSettings.QuickAlbumName;

            var album = string.IsNullOrWhiteSpace(name) ? null : ServiceLocator.DataStore.FindAlbumByName(name);

            _quickAlbumImageIds = album == null
                ? new HashSet<int>()
                : ServiceLocator.DataStore.GetAlbumImageIds(album.Id).ToHashSet();
        }
        catch (Exception)
        {
            _quickAlbumImageIds = new HashSet<int>();
        }
    }

    public bool IsInQuickAlbum(int imageId) => _quickAlbumImageIds.Contains(imageId);

    /// <summary>
    /// Lightroom style quick collection. Adds the selection to a single named album, or takes it
    /// out again when everything selected is already in there.
    /// </summary>
    /// <remarks>
    /// This is an ordinary album row, not a new concept in the database, so the original Diffusion
    /// Toolkit lists and edits it like any other album.
    /// </remarks>
    public async Task ToggleQuickAlbum()
    {
        var entries = ServiceLocator.MainModel.SelectedImages
            .Where(d => d.EntryType == EntryType.File)
            .ToList();

        if (!entries.Any()) return;

        var name = ServiceLocator.ExtendedSettings.QuickAlbumName;

        if (string.IsNullOrWhiteSpace(name)) return;

        var album = ServiceLocator.DataStore.FindAlbumByName(name)
                    ?? ServiceLocator.DataStore.CreateAlbum(new Album() { Name = name });

        var existing = ServiceLocator.DataStore.GetAlbumImageIds(album.Id).ToHashSet();

        // Everything already in the album means the shortcut should take it back out
        var isRemoval = entries.All(d => existing.Contains(d.Id));

        string message;

        if (isRemoval)
        {
            var count = ServiceLocator.DataStore.RemoveImagesFromAlbum(album.Id, entries.Select(d => d.Id));

            foreach (var entry in entries)
            {
                entry.AlbumCount = Math.Max(0, entry.AlbumCount - 1);
                entry.IsInQuickAlbum = false;
            }

            message = GetLocalizedText("Actions.Albums.QuickAlbum.Removed")
                .Replace("{images}", $"{count}")
                .Replace("{album}", album.Name);

            // Viewing the quick album itself means the entries just removed no longer belong in
            // the list. Everywhere else the badge alone is enough - refreshing there would only
            // cost a requery for no visible benefit, since nothing needs to leave the view.
            if (ServiceLocator.MainModel.CurrentAlbum is { IsQuickAlbum: true })
            {
                ServiceLocator.SearchService.RefreshResults();
            }
        }
        else
        {
            var added = entries.Where(d => !existing.Contains(d.Id)).ToList();

            ServiceLocator.DataStore.AddImagesToAlbum(album.Id, added.Select(d => d.Id));

            foreach (var entry in added)
            {
                entry.AlbumCount++;
                entry.IsInQuickAlbum = true;
            }

            message = GetLocalizedText("Actions.Albums.QuickAlbum.Added")
                .Replace("{images}", $"{added.Count}")
                .Replace("{album}", album.Name);
        }

        RefreshQuickAlbum();

        // The preview shows its own badge, so keep it in step with the thumbnails
        if (ServiceLocator.MainModel.SelectedImages.Count == 1)
        {
            var current = ServiceLocator.MainModel.CurrentImage;

            if (current != null && current.Id == entries[0].Id)
            {
                current.IsInQuickAlbum = entries[0].IsInQuickAlbum;
            }
        }

        ServiceLocator.ToastService.Toast(message, GetLocalizedText("Actions.Albums.QuickAlbum.Title"));

        UpdateSelectedImageAlbums();
        ReloadContextMenus();

        if (ReloadAlbums != null)
        {
            await ReloadAlbums();
        }
    }

    public void UpdateSelectedImageAlbums()
    {
        var ids = ServiceLocator.MainModel.SelectedImages.Where(d => d.EntryType == EntryType.File).Select(d => d.Id).ToList();

        ServiceLocator.MainModel.SelectionAlbumMenuItems = new ObservableCollection<Control>();

        if (ids.Any())
        {
            var albums = ServiceLocator.DataStore.GetImageAlbums(ids);

            foreach (var album in albums)
            {
                var menuItem = new MenuItem() { Header = album.Name, Tag = album };
                menuItem.Click += RemoveFromAlbum_OnClick;
                ServiceLocator.MainModel.SelectionAlbumMenuItems.Add(menuItem);
            }
        }
    }

    public void ReloadContextMenus()
    {
        var albumMenuItem = new MenuItem()
        {
            Header = GetLocalizedText("Thumbnail.ContextMenu.AddToAlbum.NewAlbum"),
        };

        albumMenuItem.Click += CreateAlbum_OnClick;

        //var refreshAlbumMenuItem = new MenuItem()
        //{
        //    Header = GetLocalizedText("Menu.View.Refresh"),
        //};
        //refreshAlbumMenuItem.Click += RefreshAlbum_OnClick;

        var menuItems = new List<Control>()
        {
            albumMenuItem,
            //refreshAlbumMenuItem,
            new Separator()
        };


        var albums = ServiceLocator.DataStore.GetAlbumsByName();

        foreach (var album in albums)
        {
            var menuItem = new MenuItem() { Header = album.Name, Tag = album };
            menuItem.Click += AddToAlbum_OnClick;
            menuItems.Add(menuItem);
        }

        ServiceLocator.MainModel.AlbumMenuItems = new ObservableCollection<Control>(menuItems);
    }


    private void RemoveFromAlbum_OnClick(object sender, RoutedEventArgs e)
    {
        ServiceLocator.MainModel.RemoveFromAlbumCommand?.Execute(sender);
    }

    private void CreateAlbum_OnClick(object sender, RoutedEventArgs e)
    {
        ServiceLocator.MainModel.AddAlbumCommand?.Execute(null);
    }

    private void RefreshAlbum_OnClick(object sender, RoutedEventArgs e)
    {
        ReloadContextMenus();
    }

    private void AddToAlbum_OnClick(object sender, RoutedEventArgs e)
    {
        ServiceLocator.MainModel.AddToAlbumCommand?.Execute(sender);
    }
}