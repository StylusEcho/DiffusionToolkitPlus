using System;
using System.Globalization;
using System.Windows.Input;
using Diffusion.Toolkit.Localization;

namespace Diffusion.Toolkit.Services;

public enum TagType
{
    Rating,
    NSFW,
    Favorite,
    ForDeletion
}

public class TagEventArguments
{
    public int Id { get; set; }
    public TagType TagType { get; set; }
    public object Value { get; set; }
}

public class TaggingService
{
    public event EventHandler<TagEventArguments> TagUpdated;

    /// <summary>
    /// The preview shows its tag bar over unavailable files too, but those carry no id - the
    /// preview model is built without one when the file is missing. Writing anyway updates no rows
    /// and then walks the id back through TagUpdated, where it matches the empty padding entries
    /// that also sit at id 0.
    /// </summary>
    private static bool CanTag(int id)
    {
        if (id > 0) return true;

        ServiceLocator.ToastService.Toast(GetLocalizedText("Actions.Tagging.Unavailable"), "");

        return false;
    }

    private static string GetLocalizedText(string key)
    {
        return JsonLocalizationProvider.Instance.GetLocalizedObject(key, null, CultureInfo.InvariantCulture) as string ?? key;
    }

    public void Rate(object sender, int id, int? value)
    {
        if (!CanTag(id)) return;

        ServiceLocator.DataStore.SetRating(id, value);
        TagUpdated?.Invoke(sender, new TagEventArguments() { Id =id, TagType = TagType.Rating, Value = value});
    }

    public void Favorite(object sender, int id, bool value)
    {
        if (!CanTag(id)) return;

        ServiceLocator.DataStore.SetFavorite(id, value);
        TagUpdated?.Invoke(sender, new TagEventArguments() { Id = id, TagType = TagType.Favorite, Value = value });
    }
    public void NSFW(object sender, int id, bool value)
    {
        if (!CanTag(id)) return;

        ServiceLocator.DataStore.SetNSFW(id, value);
        TagUpdated?.Invoke(sender, new TagEventArguments() { Id = id, TagType = TagType.NSFW, Value = value });
    }

    public void ForDeletion(object sender, int id, bool value)
    {
        if (!CanTag(id)) return;

        ServiceLocator.DataStore.SetDeleted(id, value);
        TagUpdated?.Invoke(sender, new TagEventArguments() { Id = id, TagType = TagType.ForDeletion, Value = value });
    }

}