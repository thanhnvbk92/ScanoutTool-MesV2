using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Media;

namespace ScanOutTool.Services
{
    public class ThemeService : IThemeService
    {
        public void ApplyTheme(string themeMode)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();

            if (string.Equals(themeMode, "Light", System.StringComparison.OrdinalIgnoreCase))
            {
                theme.SetBaseTheme(BaseTheme.Light);
                theme.SetPrimaryColor(Color.FromRgb(25, 118, 210));
                theme.SetSecondaryColor(Color.FromRgb(48, 63, 159));
                ApplySidebarColors(
                    background: Color.FromRgb(73, 69, 61),
                    itemHover: Color.FromRgb(92, 86, 76),
                    itemSelected: Color.FromRgb(112, 102, 86),
                    text: Color.FromRgb(252, 246, 232),
                    selectedText: Color.FromRgb(255, 213, 112));
                ApplySurfaceColors(
                    paper: Color.FromRgb(238, 230, 213),
                    background: Color.FromRgb(255, 251, 242),
                    divider: Color.FromRgb(209, 196, 170),
                    body: Color.FromRgb(43, 38, 31),
                    bodyLight: Color.FromRgb(116, 105, 89));
            }
            else
            {
                theme.SetBaseTheme(BaseTheme.Dark);
                theme.SetPrimaryColor(Color.FromRgb(205, 220, 57));
                theme.SetSecondaryColor(Color.FromRgb(103, 58, 183));
                ApplySidebarColors(
                    background: Color.FromRgb(33, 33, 33),
                    itemHover: Color.FromRgb(51, 51, 51),
                    itemSelected: Color.FromRgb(66, 66, 66),
                    text: Color.FromRgb(238, 238, 238),
                    selectedText: Color.FromRgb(205, 220, 57));
                ApplySurfaceColors(
                    paper: Color.FromRgb(18, 18, 18),
                    background: Color.FromRgb(33, 33, 33),
                    divider: Color.FromRgb(66, 66, 66),
                    body: Color.FromRgb(238, 238, 238),
                    bodyLight: Color.FromRgb(189, 189, 189));
            }

            paletteHelper.SetTheme(theme);
        }

        private static void ApplySidebarColors(
            Color background,
            Color itemHover,
            Color itemSelected,
            Color text,
            Color selectedText)
        {
            var resources = Application.Current?.Resources;
            if (resources == null)
            {
                return;
            }

            resources["SidebarBackgroundBrush"] = new SolidColorBrush(background);
            resources["SidebarItemHoverBrush"] = new SolidColorBrush(itemHover);
            resources["SidebarItemSelectedBrush"] = new SolidColorBrush(itemSelected);
            resources["SidebarTextBrush"] = new SolidColorBrush(text);
            resources["SidebarSelectedTextBrush"] = new SolidColorBrush(selectedText);
        }

        private static void ApplySurfaceColors(
            Color paper,
            Color background,
            Color divider,
            Color body,
            Color bodyLight)
        {
            var resources = Application.Current?.Resources;
            if (resources == null)
            {
                return;
            }

            resources["MaterialDesignPaper"] = new SolidColorBrush(paper);
            resources["MaterialDesignBackground"] = new SolidColorBrush(background);
            resources["MaterialDesignDivider"] = new SolidColorBrush(divider);
            resources["MaterialDesignBody"] = new SolidColorBrush(body);
            resources["MaterialDesignBodyLight"] = new SolidColorBrush(bodyLight);
        }
    }
}
