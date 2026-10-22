using System;
using Diffusion.Common.Query;

namespace Diffusion.Toolkit.Configuration;

/// <summary>
/// A review in progress: the view it was started against, and how far through it the user got.
/// </summary>
/// <remarks>
/// Deliberately a plain record rather than a <see cref="SettingsContainer"/>. It is rewritten on
/// every page turn, and the nested-settings pattern used elsewhere here subscribes to the child
/// on assignment without unsubscribing from the old one - fine for something set once at load,
/// a leak for something set repeatedly. <see cref="ExtendedSettings"/> raises the change on its
/// own behalf instead.
///
/// <see cref="QueryOptions"/> carries most of the view already and is known to survive a
/// round trip through System.Text.Json - saved queries store exactly this object. The rest of
/// the fields are the parts of the view it does not cover.
/// </remarks>
public class ReviewSession
{
    /// <summary>
    /// The locked query. Everything about which images are in the review except sort and paging.
    /// </summary>
    public QueryOptions? QueryOptions { get; set; }

    /// <summary>
    /// Which of the search page's modes the review was started in - images, folders, favorites or
    /// deleted. QueryOptions.SearchView does not distinguish "images" from the default, and the
    /// mode has to be applied before the query to avoid a mode switch resetting it afterwards.
    /// </summary>
    public string ModeKey { get; set; } = "images";

    public string? SortBy { get; set; }

    public string? SortDirection { get; set; }

    /// <summary>
    /// Page size the page numbers below were counted against. A later change in Settings moves
    /// every page boundary, which makes a stored page number meaningless.
    /// </summary>
    public int PageSize { get; set; }

    public int Page { get; set; } = 1;

    /// <summary>
    /// The image that was selected when the review was last left. Preferred over the page number
    /// on resume, since it survives the result set shifting underneath the review.
    /// </summary>
    public int LastImageId { get; set; }

    /// <summary>
    /// The user's own hide settings, put aside while the review runs so that marking an image
    /// cannot make it drop out of the set and shift every page behind it. Restored on exit.
    /// </summary>
    public bool SuspendedHideNSFW { get; set; }

    public bool SuspendedHideDeleted { get; set; }

    public DateTime StartedUtc { get; set; }
}
