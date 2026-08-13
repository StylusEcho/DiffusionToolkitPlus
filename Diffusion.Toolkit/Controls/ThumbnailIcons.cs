using System;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Diffusion.Common;
using Diffusion.Toolkit.Models;
using Diffusion.Toolkit.Themes;
using FontAwesome.WPF;

namespace Diffusion.Toolkit.Controls;

public class ThumbnailIcons : FrameworkElement
{
    private static BitmapImage? _starIcon;
    private static BitmapImage? _errorIcon;
    private static BitmapImage? _darkAlbumIcon;
    private static BitmapImage? _lightAlbumIcon;
    private static BitmapImage? _darkTrashIcon;
    private static BitmapImage? _lightTrashIcon;
    private static BitmapImage? _darkHideIcon;
    private static BitmapImage? _lightHideIcon;

    private static BitmapImage? _darkVideoIcon;
    private static BitmapImage? _lightVideoIcon;

    private static Typeface _typeFace = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static Typeface _typeFaceBoldCondensed = new Typeface(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Condensed);

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(ImageEntry), typeof(ThumbnailIcons),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, PropertyChangedCallback));

    private static void PropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var image = (ImageEntry)e.NewValue;
        if (image != null)
        {
            image.PropertyChanged += (sender, args) =>
            {
                switch (args.PropertyName)
                {
                    case nameof(ImageEntry.NSFW):
                    case nameof(ImageEntry.AlbumCount):
                    case nameof(ImageEntry.IsInQuickAlbum):
                    case nameof(ImageEntry.ForDeletion):
                    case nameof(ImageEntry.Favorite):
                    case nameof(ImageEntry.Rating):
                    case nameof(ImageEntry.Type):
                        var thumb = d as ThumbnailIcons;
                        thumb.InvalidateVisual();
                        break;
                }
            };
        }
    }

    public ImageEntry? Data
    {
        get => (ImageEntry)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    static ThumbnailIcons()
    {
        InitIcons();
    }

    private static Uri GetUri(string path)
    {
        return new Uri($"pack://application:,,,{path}", UriKind.RelativeOrAbsolute);
    }

    private static void InitIcons()
    {
        Uri darkTrashIconUri = GetUri("/Icons/Dark/trash-32.png");
        _darkTrashIcon = new BitmapImage(darkTrashIconUri);
        Uri lightTrashIconUri = GetUri("/Icons/Light/trash-32.png");
        _lightTrashIcon = new BitmapImage(lightTrashIconUri);
        Uri darkAlbumIconUri = GetUri("/Icons/Dark/gallery-32.png");
        _darkAlbumIcon = new BitmapImage(darkAlbumIconUri);
        Uri lightAlbumIconUri = GetUri("/Icons/Light/gallery-32.png");
        _lightAlbumIcon = new BitmapImage(lightAlbumIconUri);
        Uri starIconUri = GetUri("/Icons/star-32.png");
        _starIcon = new BitmapImage(starIconUri);
        Uri darkHideIconUri = GetUri("/Icons/Dark/hide-24.png");
        _darkHideIcon = new BitmapImage(darkHideIconUri);
        Uri lightHideIconUri = GetUri("/Icons/Light/hide-24.png");
        _lightHideIcon = new BitmapImage(lightHideIconUri);
        Uri darkVideoIconUri = GetUri("/Icons/Dark/video-24.png");
        _darkVideoIcon = new BitmapImage(darkVideoIconUri);
        Uri lightVideoIconUri = GetUri("/Icons/Light/video-24.png");
        _lightVideoIcon = new BitmapImage(lightVideoIconUri);
        Uri errorIconUri = GetUri("/Icons/error-32.png");
        _errorIcon = new BitmapImage(errorIconUri);
    }


    private static SolidColorBrush? _accentBrush;
    private static Color _accentColor;
    private static readonly Pen BadgeOutline = CreateBadgeOutline();

    private static Pen CreateBadgeOutline()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), 1);
        pen.Freeze();
        return pen;
    }

    /// <summary>
    /// The theme's accent colour, rebuilt only when the theme actually changes it.
    /// </summary>
    private static Brush AccentBrush
    {
        get
        {
            var color = Application.Current?.TryFindResource("Accent") as Color? ?? Colors.Cyan;

            if (_accentBrush == null || _accentColor != color)
            {
                _accentColor = color;
                _accentBrush = new SolidColorBrush(color);
                _accentBrush.Freeze();
            }

            return _accentBrush;
        }
    }

    /// <summary>
    /// The favourite red, which is deliberately the same in either theme.
    /// </summary>
    private static Brush FavoriteBrush
    {
        get
        {
            var color = Application.Current?.TryFindResource("Favorite") as Color? ?? Colors.Red;

            if (_favoriteBrush == null || _favoriteColor != color)
            {
                _favoriteColor = color;
                _favoriteBrush = new SolidColorBrush(color);
                _favoriteBrush.Freeze();
            }

            return _favoriteBrush;
        }
    }

    private static SolidColorBrush? _favoriteBrush;
    private static Color _favoriteColor;

    private static ImageSource? _bookmarkIcon;
    private static Color _bookmarkColor;
    private static ImageSource? _heartIcon;
    private static Color _heartColor;

    /// <summary>
    /// The same FontAwesome glyphs the preview uses, rendered once per colour rather than per frame.
    /// The accent is user-overridable at runtime, so the cache is keyed on the colour it was built
    /// with rather than filled once.
    /// </summary>
    private static ImageSource BookmarkIcon
    {
        get
        {
            var brush = (SolidColorBrush)AccentBrush;

            if (_bookmarkIcon == null || _bookmarkColor != brush.Color)
            {
                _bookmarkColor = brush.Color;
                _bookmarkIcon = ImageAwesome.CreateImageSource(FontAwesomeIcon.Bookmark, brush);
            }

            return _bookmarkIcon;
        }
    }

    private static ImageSource HeartIcon
    {
        get
        {
            var brush = (SolidColorBrush)FavoriteBrush;

            if (_heartIcon == null || _heartColor != brush.Color)
            {
                _heartColor = brush.Color;
                _heartIcon = ImageAwesome.CreateImageSource(FontAwesomeIcon.Heart, brush);
            }

            return _heartIcon;
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (Data == null)
            return;

        const int iconSize = 24;

        // The badges overlap by 2px, which is how the row has always looked
        const int xOffset = 22;

        // The control spans the whole thumbnail so the corners can be addressed independently.
        // Width is never set, so only the actual arranged size is any use here.
        var right = ActualWidth - iconSize;
        var bottom = ActualHeight - iconSize;

        // The status row runs left to right along the bottom edge
        var x = 0d;
        var y = bottom;

        // Quick album sits alone in the bottom right, clear of the row
        if (Data.IsInQuickAlbum)
        {
            drawingContext.DrawImage(BookmarkIcon, new Rect(new Point(right, bottom), new Size(iconSize, iconSize)));
        }

        //if (Data.ForDeletion)
        //{
        //    if (ThemeManager.CurrentTheme == "Dark")
        //    {
        //        drawingContext.DrawImage(_darkTrashIcon, new Rect(new Point(x, y), new Size(24, 24)));
        //    }
        //    else if (ThemeManager.CurrentTheme == "Light")
        //    {
        //        drawingContext.DrawImage(_lightTrashIcon, new Rect(new Point(x, y), new Size(24, 24)));
        //    }
        //    x += xOffset;
        //}
        if (Data.HasError)
        {
            drawingContext.DrawImage(_errorIcon, new Rect(new Point(x, y), new Size(24, 24)));
            x += xOffset;
        }



        if (Data.Type == ImageType.Video)
        {
            if (ThemeManager.CurrentTheme == "Dark")
            {
                drawingContext.DrawImage(_darkVideoIcon, new Rect(new Point(x, y), new Size(24, 24)));
            }
            else if (ThemeManager.CurrentTheme == "Light")
            {
                drawingContext.DrawImage(_lightVideoIcon, new Rect(new Point(x, y), new Size(24, 24)));
            }
            x += xOffset;
        }


        // NSFW is pinned to the top right, away from the row
        if (Data.NSFW)
        {
            if (ThemeManager.CurrentTheme == "Dark")
            {
                drawingContext.DrawImage(_darkHideIcon, new Rect(new Point(right, 0), new Size(iconSize, iconSize)));
            }
            else if (ThemeManager.CurrentTheme == "Light")
            {
                drawingContext.DrawImage(_lightHideIcon, new Rect(new Point(right, 0), new Size(iconSize, iconSize)));
            }
        }

        // The quick album is an ordinary album row, so it counts towards AlbumCount. Discount it
        // here or filing something with the shortcut would badge it twice over.
        if (Data.AlbumCount > (Data.IsInQuickAlbum ? 1 : 0))
        {
            if (ThemeManager.CurrentTheme == "Dark")
            {
                drawingContext.DrawImage(_darkAlbumIcon, new Rect(new Point(x, y), new Size(24, 24)));
            }
            else if (ThemeManager.CurrentTheme == "Light")
            {
                drawingContext.DrawImage(_lightAlbumIcon, new Rect(new Point(x, y), new Size(24, 24)));
            }
            x += xOffset;
        }

        if (Data.Favorite)
        {
            drawingContext.DrawImage(HeartIcon, new Rect(new Point(x, y), new Size(iconSize, iconSize)));
            x += xOffset;
        }

        if (Data.Rating.HasValue)
        {
            drawingContext.DrawImage(_starIcon, new Rect(new Point(x, y), new Size(iconSize, iconSize)));
            var value = Data.Rating.Value.ToString();

            var fontSize = 14;
            Typeface typeface = _typeFaceBoldCondensed;

            var formattedText = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, null, TextFormattingMode.Display, 92)
            {
                TextAlignment = TextAlignment.Center
            };
            if (Data.Rating.Value == 10)
            {
                x += 3;
            }
            drawingContext.DrawText(formattedText, new Point(x + 15 - formattedText.WidthIncludingTrailingWhitespace / 2, y + 5));
            x += xOffset;
        }


    }
}