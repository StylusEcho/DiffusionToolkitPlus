using System;
using System.Collections.Generic;
using Diffusion.Database;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Input;
using Diffusion.Toolkit.Classes;
using Diffusion.Toolkit.Configuration;
using Diffusion.Toolkit.Controls;
using Diffusion.Common.Query;

namespace Diffusion.Toolkit.Models;

public class MainModel : BaseNotify
{
    private ICommand _removeMarked;
    private string _status;
    private bool _isPreviewVisible;
    private ObservableCollection<ImageEntry>? _selectedImages;
    private ObservableCollection<FolderViewModel> _folders;


    private ICommand _showSettingsCommand;

    public MainModel()
    {
        _status = "Ready";
        _isPreviewVisible = true;
        _selectedImages = new ObservableCollection<ImageEntry>();
        _queryOptions = new QueryOptions();
        _folders = new ObservableCollection<FolderViewModel>();
    }

    public QueryOptions QueryOptions
    {
        get => _queryOptions;
        set => SetField(ref _queryOptions, value);
    }

    public string QueryText
    {
        get;
        set => SetField(ref field, value);
    }

    public bool HasQuery
    {
        get;
        set => SetField(ref field, value);
    }

    public bool HasFilter
    {
        get;
        set => SetField(ref field, value);
    }

    public Page Page
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand Rescan
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand RenameFileCommand { get; set; }

    public ICommand OpenWithCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand SettingsCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand CloseCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand Rebuild
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ReloadHashes
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowIcons
    {
        get;
        set => SetField(ref field, value);
    }

    public bool HideIcons
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand GotoUrl
    {
        get;
        set => SetField(ref field, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    /// <summary>
    /// Mirrors the sidecar auto-hide setting so the toggle button on the view bar can show its state.
    /// </summary>
    public bool IsNavigationAutoHideEnabled
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Turns navigation pane auto-hide on and off. Bound to the button at the foot of the view bar.
    /// </summary>
    public ICommand ToggleNavigationAutoHideCommand
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// True while the library is being read at startup. The search page shows a progress overlay
    /// and holds off searching until this clears.
    /// </summary>
    public bool IsLoadingLibrary
    {
        get;
        set => SetField(ref field, value);
    }

    public string LibraryLoadStatus
    {
        get;
        set => SetField(ref field, value);
    }

    public int LibraryLoadProgress
    {
        get;
        set => SetField(ref field, value);
    }

    public int LibraryLoadTotal
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Marks the start of the startup load, with the number of steps that will be reported.
    /// </summary>
    public void StartLibraryLoad(int steps, string status)
    {
        LibraryLoadTotal = steps;
        LibraryLoadProgress = 0;
        LibraryLoadStatus = status;
        IsLoadingLibrary = true;
    }

    /// <summary>
    /// Advances the bar only. The caption is set once at the start and left alone: the steps
    /// complete out of order, and naming the one that just finished told the user nothing.
    /// </summary>
    public void AdvanceLibraryLoad()
    {
        LibraryLoadProgress++;
    }

    public void EndLibraryLoad()
    {
        LibraryLoadProgress = LibraryLoadTotal;
        IsLoadingLibrary = false;
        LibraryLoadStatus = string.Empty;
    }

    public bool IsBusy
    {
        get;
        set
        {
            SetField(ref field, value);
            OnPropertyChanged(nameof(HasPendingTask));
        }
    }

    public bool HasQueued
    {
        get;
        set
        {
            SetField(ref field, value);
            OnPropertyChanged(nameof(HasPendingTask));
        }
    }

    public bool HasPendingTask => IsBusy || HasQueued;

    public int CurrentProgress
    {
        get;
        set => SetField(ref field, value);
    }

    public int TotalProgress
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand CancelCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand AboutCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ReleaseNotesCommand { get; set; }

    public ICommand HelpCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ToggleInfoCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ToggleNSFWBlurCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ToggleVisibilityCommand
    {
        get;
        set;
    }

    public ICommand ToggleTagsCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowTags
    {
        get;
        set => SetField(ref field, value);
    }

    public bool AutoAdvance
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ToggleAutoAdvance
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ToggleHideNSFW
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ToggleHideDeleted
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ToggleHideUnavailable
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ToggleFilenamesCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public bool NSFWBlur
    {
        get;
        set => SetField(ref field, value);
    }

    public bool HideNSFW
    {
        get;
        set => SetField(ref field, value);
    }

    public bool HideDeleted
    {
        get;
        set => SetField(ref field, value);
    }

    public bool HideUnavailable
    {
        get;
        set => SetField(ref field, value);
    }

    public bool FitToPreview
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ToggleFitToPreview
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ActualSize
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ToggleActualSize
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand SetThumbnailSize
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand PoputPreview
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ResetLayout
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand TogglePreview
    {
        get;
        set => SetField(ref field, value);
    }

    public bool IsPreviewVisible
    {
        get => _isPreviewVisible;
        set => SetField(ref _isPreviewVisible, value);
    }

    public ICommand SaveQuery
    {
        get;
        set;
    }

    /// <summary>
    /// Starts a review, leaves the running one, or picks the last one back up - whichever the
    /// current state calls for.
    /// </summary>
    public ICommand ToggleReviewCommand
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Throws away a paused review rather than resuming it.
    /// </summary>
    public ICommand DiscardReviewCommand
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// True while a review is running. The controls that would change which images are on screen
    /// are locked out for the duration, so the user can work through the pages without the set
    /// moving underneath them.
    /// </summary>
    public bool IsReviewing
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// True when there is a review to go back to, running or paused. Drives whether the button
    /// offers to start one or to resume.
    /// </summary>
    public bool HasReviewSession
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Marking and navigation that until now only existed inside the thumbnail grid's own key
    /// handler, surfaced as commands so anything else - a menu, a key binding, an external
    /// controller - can reach them.
    /// </summary>
    public ICommand RateSelectedCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand UnrateSelectedCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand FavoriteSelectedCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand NSFWSelectedCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand DeleteSelectedCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand NextImageCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand PreviousImageCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand NextPageCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand PreviousPageCommand
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Opens the quick album as a view, rather than adding to it.
    /// </summary>
    public ICommand OpenQuickAlbumCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand RescanResults
    {
        get;
        set;
    }

    public ICommand AddAllToAlbum
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand MarkAllForDeletion
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand UnmarkAllForDeletion
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand RemoveMatching
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand AutoTagNSFW
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ClearModelsCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public bool HasSelectedModels
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ClearAlbumsCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ClearTagsCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public bool HasSelectedAlbums
    {
        get;
        set => SetField(ref field, value);
    }

    public bool HasSelectedTags
    {
        get;
        set => SetField(ref field, value);
    }

    /// <summary>
    /// Adds or removes the selection from the quick album. Bound to B.
    /// </summary>
    public ICommand ToggleQuickAlbumCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand SortAlbumCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand Refresh
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand QuickCopy
    {
        get;
        set => SetField(ref field, value);
    }

    public int ThumbnailSize
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand Escape
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand DownloadCivitai
    {
        get;
        set => SetField(ref field, value);
    }

    public ObservableCollection<AlbumModel> Albums
    {
        get;
        set => SetField(ref field, value);
    }
    
    public ObservableCollection<TagFilterView> Tags
    {
        get;
        set => SetField(ref field, value);
    }

    public ObservableCollection<QueryModel> Queries
    {
        get;
        set => SetField(ref field, value);
    }

    public QueryModel? SelectedQuery
    {
        get;
        set => SetField(ref field, value);
    }

    public int SelectedAlbumsCount
    {
        get;
        set => SetField(ref field, value);
    }

    public int SelectedTagsCount
    {
        get;
        set => SetField(ref field, value);
    }

    public int SelectedModelsCount
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand AddAlbumCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand AddToAlbumCommand
    {
        get;
        set => SetField(ref field, value);
    }


    public ICommand RemoveFromAlbumCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand RenameAlbumCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand AddTagCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand RenameTagCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand RemoveAlbumCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand RemoveTagCommand
    {
        get;
        set => SetField(ref field, value);
    }


    public ICommand RenameQueryCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand RemoveQueryCommand
    {
        get;
        set => SetField(ref field, value);
    }


    public ObservableCollection<ImageEntry>? SelectedImages
    {
        get => _selectedImages;
        set => SetField(ref _selectedImages, value);
    }

    public AlbumListItem? SelectedAlbum
    {
        get;
        set => SetField(ref field, value);
    }

    public AlbumModel? CurrentAlbum
    {
        get;
        set => SetField(ref field, value);
    }

    private int _progressTarget;
    private QueryOptions _queryOptions;

    public ModelViewModel? CurrentModel
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand CreateAlbumCommand { get; set; }

    public ICommand ImportTagsCommand { get; set; }

    public ICommand ExportTagsCommand { get; set; }

    public ICommand CreateTagCommand { get; set; }

    public Action<IAlbumInfo> AddSelectedImagesToAlbum { get; set; }


    public ICommand FixFoldersCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand RemoveExcludedImagesCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand CleanRemovedFoldersCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand UnavailableFilesCommand
    {
        get;
        set => SetField(ref field, value);
    }


    public ICommand ShowFilterCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ToggleAutoRefresh
    {
        get;
        set => SetField(ref field, value);
    }

    public bool AutoRefresh
    {
        get;
        set => SetField(ref field, value);
    }


    public ObservableCollection<FolderViewModel> Folders
    {
        get => _folders;
        set => SetField(ref _folders, value);
    }

    public IEnumerable<ModelViewModel>? ImageModels
    {
        get;
        set => SetField(ref field, value);
    }

    public IEnumerable<string> ImageModelNames
    {
        get;
        set => SetField(ref field, value);
    }

    public string ActiveView
    {
        get;
        set => SetField(ref field, value);
    }

    public Settings Settings
    {
        get;
        set => SetField(ref field, value);
    }

    public Action<FolderViewModel> MoveSelectedImagesToFolder { get; set; }


    public FolderViewModel? CurrentFolder
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ReloadFoldersCommand
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ScanFolderCommand { get; set; }
    public ICommand RescanFolderCommand { get; set; }
    public ICommand CreateFolderCommand { get; set; }
    public ICommand RenameFolderCommand { get; set; }
    public ICommand RenameFolderDisplayNameCommand { get; set; }
    public ICommand RemoveFolderCommand { get; set; }
    public ICommand DeleteFolderCommand { get; set; }
    public ICommand ArchiveFolderCommand { get; set; }
    public ICommand ArchiveFolderRecursiveCommand { get; set; }
    public ICommand ExcludeFolderCommand { get; set; }
    public ICommand ExcludeFolderRecursiveCommand { get; set; }



    public ICommand ToggleNavigationPane { get; set; }
    public ICommand ShowInExplorerCommand { get; set; }

    public string ToastMessage
    {
        get;
        set => SetField(ref field, value);
    }

    public QueryModel CurrentQuery { get; set; }

    public bool IsSettingsDirty
    {
        get;
        set => SetField(ref field, value);
    }

    public ObservableCollection<Control> OpenWithMenuItems
    {
        get;
        set => SetField(ref field, value);
    }

    public bool FoldersBusy
    {
        get;
        set => SetField(ref field, value);
    }

    public bool ShowNotifications
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ToggleNotificationsCommand { get; set; }

    public ICommand NavigateToParentFolderCommand { get; set; }

    public bool PermanentlyDelete
    {
        get;
        set => SetField(ref field, value);
    }

    public ObservableCollection<Control> AlbumMenuItems
    {
        get;
        set => SetField(ref field, value);
    }

    public ObservableCollection<Control> SelectionAlbumMenuItems
    {
        get;
        set => SetField(ref field, value);
    }


    public bool ShowFilenames
    {
        get;
        set => SetField(ref field, value);
    }

    // TODO: Consolidate these
    public ImageEntry SelectedImageEntry
    {
        get;
        set => SetField(ref field, value);
    }

    public ImageEntry CurrentImageEntry
    {
        get;
        set => SetField(ref field, value);
    }

    public ImageViewModel CurrentImage { get; set; }

    public ThumbnailViewMode ThumbnailViewMode
    {
        get;
        set => SetField(ref field, value);
    }

    public ICommand ToggleThumbnailViewModeCommand { get; set;  }
    public ICommand FocusSearch { get; set; }
    public ICommand RefreshFolderCommand { get; set; }
    public bool IsPreviewOpen { get; set; }
}