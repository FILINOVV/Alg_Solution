using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using SkiaSharp;

namespace LabApp.Ui
{
    // Цвета для графиков. LiveCharts рисует через SkiaSharp и не видит WPF-ресурсы,
    // поэтому палитру для графиков держим отдельно, в коде.
    public record ChartPalette(SKColor Text, SKColor Separator);

    public static class AppTheme
    {
        public static bool IsDark { get; private set; }

        // Подписываются те, кому нужно перекраситься вручную (графики, кнопка темы)
        public static event Action? ThemeChanged;

        public static readonly ChartPalette LightChart = new(SKColor.Parse("#6B7280"), SKColor.Parse("#E4E7EC"));
        public static readonly ChartPalette DarkChart = new(SKColor.Parse("#9AA3B2"), SKColor.Parse("#2A2F3A"));
        public static ChartPalette Chart => IsDark ? DarkChart : LightChart;

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LabApp", "theme.txt");

        public static void Apply(bool dark)
        {
            IsDark = dark;

            // Меняем словарь с цветами. Всё, что взято через DynamicResource, перекрасится само
            var palette = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Absolute)
            };
            var merged = Application.Current.Resources.MergedDictionaries;
            if (merged.Count > 0) merged[0] = palette;
            else merged.Add(palette);

            foreach (Window window in Application.Current.Windows)
                SetTitleBar(window, dark);

            ThemeChanged?.Invoke();
        }

        public static void Toggle()
        {
            Apply(!IsDark);
            SavePreference();
        }

        // Выбор пользователя храним в %AppData%\LabApp\theme.txt.
        // При первом запуске берём тему Windows.
        public static bool LoadPreference()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return File.ReadAllText(SettingsPath).Trim() == "dark";

                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch
            {
                return false;
            }
        }

        private static void SavePreference()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, IsDark ? "dark" : "light");
            }
            catch
            {
                // Не смогли сохранить — не страшно, просто в следующий раз откроется тема по умолчанию
            }
        }

        // Тёмная полоса заголовка окна (Windows 10 20H1+ и Windows 11)
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DwmUseImmersiveDarkMode = 20;

        public static void SetTitleBar(Window window, bool dark)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return; // окно ещё не создано — вызовем позже из SourceInitialized
            int value = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref value, sizeof(int));
        }
    }
}
