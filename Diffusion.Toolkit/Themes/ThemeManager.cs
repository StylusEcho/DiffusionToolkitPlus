using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Media;
using Diffusion.Toolkit.Services;

namespace Diffusion.Toolkit.Themes
{
    public static class ThemeManager
    {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private const string RegistryValueName = "AppsUseLightTheme";

        public static string CurrentTheme { get; private set; }

        public static void ChangeTheme(string themeName)
        {
            if (string.IsNullOrEmpty(themeName) || themeName  == "System")
            {
                themeName = GetWindowsTheme();
            }

            CurrentTheme = themeName;

            var app = (App)Application.Current;
            app.Resources.MergedDictionaries.Clear();

            LoadResourceDictionary(app, "Themes/ToolTips.xaml");
            LoadResourceDictionary(app, $"Themes/{themeName}.xaml");
            LoadResourceDictionary(app, "Themes/Common.xaml");
            LoadResourceDictionary(app, "Themes/Menu.xaml");
            LoadResourceDictionary(app, "Themes/SWStyles.xaml");
            LoadResourceDictionary(app, "Themes/Scrollbars.xaml");
            LoadResourceDictionary(app, "Themes/Window.xaml");

            ApplyAccentOverride(app);
        }

        /// <summary>
        /// Applies the user's accent colour over the one the theme ships with.
        /// </summary>
        /// <remarks>
        /// Has to run after the dictionaries are loaded, and again on every theme change, because
        /// ChangeTheme clears and rebuilds them. Writing the key directly into app.Resources rather
        /// than into a merged dictionary is what makes it win: entries held directly by a
        /// ResourceDictionary take precedence over entries in its MergedDictionaries, and the
        /// AccentBrush is a DynamicResource so it repoints without anything being rebuilt.
        /// </remarks>
        public static void ApplyAccentOverride(Application? app = null)
        {
            app ??= Application.Current;

            if (app == null) return;

            var accent = ParseAccent(ServiceLocator.ExtendedSettings?.AccentColor);

            if (accent.HasValue)
            {
                var brush = new SolidColorBrush(accent.Value);
                brush.Freeze();

                app.Resources["Accent"] = accent.Value;

                // Set the brush as well as the colour. Common.xaml derives AccentBrush from Accent
                // with a DynamicResource, but overriding it outright means the new colour does not
                // depend on that reference re-resolving.
                app.Resources["AccentBrush"] = brush;
            }
            else
            {
                // Falls back to the theme dictionary's own Accent and Common.xaml's AccentBrush
                app.Resources.Remove("Accent");
                app.Resources.Remove("AccentBrush");
            }
        }

        /// <summary>
        /// Reads a hex colour, returning null for anything empty or malformed so a bad value in the
        /// settings file leaves the theme's own accent in place rather than throwing on startup.
        /// </summary>
        public static Color? ParseAccent(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            try
            {
                return ColorConverter.ConvertFromString(value.Trim()) as Color?;
            }
            catch (Exception)
            {
                // The value is whatever the user has typed so far, so anything unparseable is
                // expected rather than exceptional - fall back to the theme's accent
                return null;
            }
        }

        private static void LoadResourceDictionary(App app, string url)
        {
            var resource = (ResourceDictionary)Application.LoadComponent(new Uri(url, UriKind.Relative));
            app.Resources.MergedDictionaries.Add(resource);
        }

        private static string GetWindowsTheme()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            var registryValueObject = key?.GetValue(RegistryValueName);
            if (registryValueObject == null)
            {
                return "Light";
            }
            var registryValue = (int)registryValueObject;

            return registryValue > 0 ? "Light" : "Dark";
        }

    }
}
