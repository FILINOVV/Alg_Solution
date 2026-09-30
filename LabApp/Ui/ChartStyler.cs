using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.SKCharts;
using SkiaSharp;
using WpfChart = LiveChartsCore.SkiaSharpView.WPF.CartesianChart;

namespace LabApp.Ui
{
    public static class ChartStyler
    {
        // Подписи осей, цвета текста и сетки под текущую тему, легенда снизу
        public static void Apply(WpfChart chart, string xTitle, string yTitle, ChartPalette palette)
        {
            chart.XAxes = new[] { BuildAxis(xTitle, palette, 12, 13) };
            chart.YAxes = new[] { BuildAxis(yTitle, palette, 12, 13) };
            chart.LegendPosition = LegendPosition.Bottom;
            chart.LegendTextPaint = new SolidColorPaint(palette.Text);
        }

        // Сохранение графика в PNG. Всегда в светлых цветах на белом фоне —
        // так картинка нормально выглядит в отчёте и при печати, даже если в программе тёмная тема.
        public static void ExportPng(WpfChart chart, string path, string xTitle, string yTitle)
        {
            var palette = AppTheme.LightChart;
            var image = new SKCartesianChart(chart)
            {
                Width = 1400,
                Height = 800,
                Background = SKColors.White,
                XAxes = new[] { BuildAxis(xTitle, palette, 18, 20) },
                YAxes = new[] { BuildAxis(yTitle, palette, 18, 20) },
                LegendPosition = LegendPosition.Bottom,
                LegendTextPaint = new SolidColorPaint(SKColor.Parse("#1C2333")),
            };
            image.SaveImage(path);
        }

        private static Axis BuildAxis(string title, ChartPalette palette, double labelSize, double titleSize) => new()
        {
            Name = title,
            NamePaint = new SolidColorPaint(palette.Text),
            NameTextSize = titleSize,
            LabelsPaint = new SolidColorPaint(palette.Text),
            TextSize = labelSize,
            SeparatorsPaint = new SolidColorPaint(palette.Separator, 1),
            Labeler = FormatNumber
        };

        // 2000000 -> «2 000 000», 0.00012 -> «0,00012», 12.3456 -> «12,35»
        private static string FormatNumber(double value) =>
            Math.Abs(value) >= 1000 ? value.ToString("N0") : value.ToString("G4");
    }
}
