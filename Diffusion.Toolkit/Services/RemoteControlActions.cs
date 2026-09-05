using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Diffusion.Common;

namespace Diffusion.Toolkit.Services;

/// <summary>
/// Maps the short action names an external controller sends onto the commands the application
/// already has.
/// </summary>
/// <remarks>
/// Deliberately a curated list rather than a generic "run any command by name". Most of the
/// command surface opens dialogs, which would leave a controller waiting on a click it cannot make,
/// and a fixed list is something a plugin can be written against.
/// </remarks>
public class RemoteControlActions
{
    public readonly record struct Result(bool Ok, string? Error)
    {
        public static Result Success => new(true, null);

        public static Result Fail(string error) => new(false, error);
    }

    private readonly Dictionary<string, Action<JsonElement?>> _actions;

    /// <summary>
    /// Actions that change which images are on screen. A review deliberately locks these out, the
    /// same way it disables the controls that would do it from the UI.
    /// </summary>
    private static readonly HashSet<string> ChangesTheResultSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "view.folders", "view.images", "view.favorites", "view.deleted",
        "quickalbum.open", "filter.type", "filter.clear"
    };

    public RemoteControlActions()
    {
        _actions = new Dictionary<string, Action<JsonElement?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["rate"] = value => Execute(Main?.RateSelectedCommand, RequireRating(value)),
            ["unrate"] = _ => Execute(Main?.UnrateSelectedCommand),
            ["favorite"] = _ => Execute(Main?.FavoriteSelectedCommand),
            ["nsfw"] = _ => Execute(Main?.NSFWSelectedCommand),
            ["delete"] = _ => Execute(Main?.DeleteSelectedCommand),

            ["nav.next"] = _ => Execute(Main?.NextImageCommand),
            ["nav.prev"] = _ => Execute(Main?.PreviousImageCommand),
            ["page.next"] = _ => Execute(Main?.NextPageCommand),
            ["page.prev"] = _ => Execute(Main?.PreviousPageCommand),

            ["view.folders"] = _ => Execute(Main?.GotoUrl, "search/#folders"),
            ["view.images"] = _ => Execute(Main?.GotoUrl, "search/#images"),
            ["view.favorites"] = _ => Execute(Main?.GotoUrl, "search/#favorites"),
            ["view.deleted"] = _ => Execute(Main?.GotoUrl, "search/#deleted"),

            ["quickalbum.toggle"] = _ => Execute(Main?.ToggleQuickAlbumCommand),
            ["quickalbum.open"] = _ => Execute(Main?.OpenQuickAlbumCommand),

            ["review.toggle"] = _ => Execute(Main?.ToggleReviewCommand),
            ["info.toggle"] = _ => Execute(Main?.ToggleInfoCommand),
            ["zoom.fit"] = _ => Execute(Main?.ToggleFitToPreview),
            ["zoom.actual"] = _ => Execute(Main?.ToggleActualSize),
            ["autoadvance.toggle"] = _ => Execute(Main?.ToggleAutoAdvance),
            ["refresh"] = _ => Execute(Main?.Refresh),

            // Not MainModel.ShowInExplorerCommand - that one takes a folder from the folders tree.
            // This is the same call the image context menu makes, and it already reports a file
            // that has gone away rather than throwing.
            ["explorer.show"] = _ => ServiceLocator.ContextMenuService.ShowInExplorer(null!),

            ["filter.type"] = value => Execute(Search?.ToggleTypeFilterCommand, RequireMediaType(value)),
            ["filter.clear"] = _ => Execute(Search?.ClearSearch)
        };
    }

    private static Models.MainModel? Main => ServiceLocator.MainModel;

    private static Models.SearchModel? Search => ServiceLocator.SearchModel;

    /// <summary>
    /// Runs one action on the UI thread and reports whether it was accepted.
    /// </summary>
    public Task<Result> InvokeAsync(string action, JsonElement? value)
    {
        if (!_actions.TryGetValue(action, out var handler))
        {
            return Task.FromResult(Result.Fail("unknown action"));
        }

        if (Main == null)
        {
            return Task.FromResult(Result.Fail("not ready"));
        }

        if (Main.IsReviewing && ChangesTheResultSet.Contains(action))
        {
            // Saying so beats a button that silently does nothing
            return Task.FromResult(Result.Fail("locked while reviewing"));
        }

        var dispatcher = ServiceLocator.Dispatcher;

        if (dispatcher == null)
        {
            return Task.FromResult(Result.Fail("not ready"));
        }

        var completion = new TaskCompletionSource<Result>();

        dispatcher.InvokeAsync(() =>
        {
            try
            {
                handler(value);

                completion.SetResult(Result.Success);
            }
            catch (ArgumentException e)
            {
                completion.SetResult(Result.Fail(e.Message));
            }
            catch (Exception e)
            {
                Logger.Log($"Remote control action '{action}' failed: {e.Message}");

                completion.SetResult(Result.Fail("failed"));
            }
        });

        return completion.Task;
    }

    private static void Execute(ICommand? command, object? parameter = null)
    {
        if (command == null) throw new ArgumentException("not available");

        if (!command.CanExecute(parameter)) throw new ArgumentException("not available right now");

        command.Execute(parameter);
    }

    private static object RequireRating(JsonElement? value)
    {
        if (value is { ValueKind: JsonValueKind.Number } element && element.TryGetInt32(out var rating))
        {
            if (rating is < 1 or > 10) throw new ArgumentException("rating must be 1 to 10");

            return rating;
        }

        throw new ArgumentException("rating must be 1 to 10");
    }

    /// <summary>
    /// The filter command takes the same strings the two buttons in the search bar pass.
    /// </summary>
    private static object RequireMediaType(JsonElement? value)
    {
        var text = value is { ValueKind: JsonValueKind.String } element ? element.GetString() : null;

        if (string.Equals(text, "Image", StringComparison.OrdinalIgnoreCase)) return "Image";
        if (string.Equals(text, "Video", StringComparison.OrdinalIgnoreCase)) return "Video";

        throw new ArgumentException("value must be Image or Video");
    }
}
