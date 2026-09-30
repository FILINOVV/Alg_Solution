using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LabApp.Algorithms;
using LabApp.Models;
using LabApp.Services;
using LabApp.Ui;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using WpfChart = LiveChartsCore.SkiaSharpView.WPF.CartesianChart;

namespace LabApp
{
    public class HistoryItemVm
    {
        public int Id { get; set; }
        public string Label { get; set; } = "";
        public bool IsChecked { get; set; }
    }

    public partial class MainWindow : Window
    {
        private readonly DatabaseService _db = new();
        private readonly BenchmarkService _benchmark;
        private readonly MatrixBenchmarkService _matrixBenchmark;
        private readonly PowerBenchmarkService _powerBenchmark;

        // Цвета линий подобраны так, чтобы читаться и на светлом, и на тёмном фоне
        private static readonly SKColor[] Palette =
        {
            SKColor.Parse("#3B82F6"), SKColor.Parse("#F97316"), SKColor.Parse("#10B981"),
            SKColor.Parse("#A855F7"), SKColor.Parse("#EAB308"), SKColor.Parse("#EF4444")
        };

        // Подписи осей каждого графика — нужны при смене темы и при экспорте в PNG
        private readonly Dictionary<WpfChart, (string X, string Y)> _chartTitles = new();

        public MainWindow()
        {
            InitializeComponent();
            _benchmark = new BenchmarkService(_db);
            _matrixBenchmark = new MatrixBenchmarkService(_db);
            _powerBenchmark = new PowerBenchmarkService(_db);

            AlgorithmCombo.ItemsSource = AlgorithmCatalog.All;
            AlgorithmCombo.SelectedIndex = 1;

            // Тема: тёмный заголовок окна + перекраска графиков при переключении
            SourceInitialized += (_, _) => AppTheme.SetTitleBar(this, AppTheme.IsDark);
            AppTheme.ThemeChanged += OnThemeChanged;
            Closed += (_, _) => AppTheme.ThemeChanged -= OnThemeChanged;

            StyleChart(ResultChart, "Размер вектора n", "Время одного запуска, мс");
            StyleChart(MatrixChart, "Размер матрицы n", "Время, мс");
            StyleChart(PowerChart, "Показатель степени n", "Количество умножений");
            StyleChart(HistoryChart, "n", "Время, мс (для степеней — умножения)");
            UpdateThemeButton();
        }

        // ================= Тема =================

        private void ThemeToggle_Click(object sender, RoutedEventArgs e) => AppTheme.Toggle();

        private void OnThemeChanged()
        {
            foreach (var (chart, titles) in _chartTitles)
                ChartStyler.Apply(chart, titles.X, titles.Y, AppTheme.Chart);
            UpdateThemeButton();
        }

        private void UpdateThemeButton()
        {
            // Иконки из шрифта Segoe MDL2 Assets: E706 — солнце, E708 — луна
            ThemeIcon.Text = AppTheme.IsDark ? "\uE706" : "\uE708";
            ThemeLabel.Text = AppTheme.IsDark ? "Светлая тема" : "Тёмная тема";
        }

        private void StyleChart(WpfChart chart, string xTitle, string yTitle)
        {
            _chartTitles[chart] = (xTitle, yTitle);
            ChartStyler.Apply(chart, xTitle, yTitle, AppTheme.Chart);
        }

        // Время бывает от наносекунд (O(1)) до секунд (пузырёк), поэтому единицу
        // на оси Y подбираем по самому большому значению, чтобы не было подписей вида 1E-06
        private static (double Factor, string Unit) PickTimeUnit(double maxMs) =>
            maxMs >= 1 ? (1.0, "мс") :
            maxMs >= 1e-3 ? (1e3, "мкс") :
            (1e6, "нс");

        // ================= Экспорт графика в PNG =================

        private void ExportChart_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: string chartName } || FindName(chartName) is not WpfChart chart)
                return;

            if (chart.Series is null || !chart.Series.Any())
            {
                MessageBox.Show("Сначала запустите эксперимент — график пока пустой.");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PNG-изображение (*.png)|*.png",
                FileName = $"{chartName}_{DateTime.Now:yyyy-MM-dd_HH-mm}.png"
            };
            if (dialog.ShowDialog(this) != true) return;

            var (xTitle, yTitle) = _chartTitles.TryGetValue(chart, out var titles) ? titles : ("n", "");
            try
            {
                ChartStyler.ExportPng(chart, dialog.FileName, xTitle, yTitle);
                MessageBox.Show($"График сохранён:\n{dialog.FileName}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось сохранить график: {ex.Message}");
            }
        }

        private void Nav_Changed(object sender, RoutedEventArgs e)
        {
            if (VectorPanel == null) return;

            VectorPanel.Visibility = NavVector.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            MatrixPanel.Visibility = NavMatrix.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            PowerPanel.Visibility = NavPower.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            HistoryPanel.Visibility = NavHistory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            if (NavHistory.IsChecked == true) LoadHistoryList();
        }

        private void AlgorithmCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AlgorithmCombo.SelectedItem is not AlgorithmDefinition algo) return;

            InfoNameText.Text = algo.Name;
            InfoComplexityText.Text = algo.ComplexityLabel;
            InfoDescriptionText.Text = algo.Description;

            int recommended = AlgorithmCatalog.RecommendedNMax(algo.Complexity);
            RecommendedNMaxText.Text = $"Рекомендуемый N max для этого класса сложности: ~{recommended:N0}";
        }

        private void RunButton_Click(object sender, RoutedEventArgs e)
        {
            if (AlgorithmCombo.SelectedItem is not AlgorithmDefinition algo)
            {
                MessageBox.Show("Выберите алгоритм.");
                return;
            }
            if (!TryParsePositiveInts(out int nMax, out int step, out int runs,
                    NMaxBox.Text, StepBox.Text, RunsBox.Text))
            {
                MessageBox.Show("Проверьте параметры: N max, шаг и количество запусков должны быть положительными числами.");
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                int runId = _db.CreateRun(algo.Key, algo.Name, nMax, step, runs);
                var results = _benchmark.RunExperiment(algo, nMax, step, runs, UseCacheBox.IsChecked == true, runId);
                RenderVectorResults(algo, results);
            }
            finally { Mouse.OverrideCursor = null; }
        }

        private void RenderVectorResults(AlgorithmDefinition algo, List<ExperimentResult> results)
        {
            ResultsGrid.ItemsSource = results;

            var (c, _) = BenchmarkService.Approximate(results, algo.Complexity);
            var (factor, unit) = PickTimeUnit(results.Count > 0 ? results.Max(r => r.AvgTimeMs) : 0);

            var empirical = results.Select(r => new ObservablePoint(r.N, r.AvgTimeMs * factor)).ToArray();
            var theoretical = results
                .Select(r => new ObservablePoint(r.N, c * BenchmarkService.TheoreticalF(algo.Complexity, r.N) * factor))
                .ToArray();

            ResultChart.Series = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = empirical, Name = "Эксперимент (среднее)", Fill = null,
                    GeometrySize = 5, Stroke = new SolidColorPaint(Palette[0], 2),
                    GeometryStroke = new SolidColorPaint(Palette[0], 2)
                },
                new LineSeries<ObservablePoint>
                {
                    Values = theoretical, Name = $"Теоретическая кривая {algo.ComplexityLabel}", Fill = null,
                    GeometrySize = 0, Stroke = new SolidColorPaint(Palette[1], 2)
                }
            };

            StyleChart(ResultChart, "Размер вектора n", $"Время одного запуска, {unit}");

        }

        private void MatrixRunButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParsePositiveInts(out int nMax, out int step, out int runs,
                    MatrixNMaxBox.Text, MatrixStepBox.Text, MatrixRunsBox.Text))
            {
                MessageBox.Show("Проверьте параметры N max, шаг и количество запусков.");
                return;
            }

            int[] mValues;
            try
            {
                mValues = MatrixMValuesBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(int.Parse).Where(v => v > 0).Distinct().ToArray();
                if (mValues.Length == 0) throw new Exception();
            }
            catch
            {
                MessageBox.Show("Значения m должны быть положительными числами через запятую, например: 20,50,100");
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                int runId = _db.CreateRun("matrix", "Умножение матриц", nMax, step, runs);
                var series = _matrixBenchmark.RunExperiment(nMax, step, runs, MatrixUseCacheBox.IsChecked == true, mValues, runId);
                RenderMatrixResults(series);
            }
            finally { Mouse.OverrideCursor = null; }
        }

        private void RenderMatrixResults(List<MatrixSeriesResult> series)
        {
            var tableRows = new List<object>();
            var chartSeries = new List<ISeries>();

            double maxMs = series.SelectMany(ser => ser.Points).Select(pt => pt.AvgTimeMs).DefaultIfEmpty(0).Max();
            var (factor, unit) = PickTimeUnit(maxMs);

            for (int i = 0; i < series.Count; i++)
            {
                var s = series[i];
                var color = Palette[i % Palette.Length];

                chartSeries.Add(new LineSeries<ObservablePoint>
                {
                    Values = s.Points.Select(p => new ObservablePoint(p.N, p.AvgTimeMs * factor)).ToArray(),
                    Name = $"m = {s.M}",
                    Fill = null,
                    GeometrySize = 4,
                    Stroke = new SolidColorPaint(color, 2),
                    GeometryStroke = new SolidColorPaint(color, 2)
                });

                foreach (var p in s.Points)
                    tableRows.Add(new { p.N, M = s.M, p.AvgTimeMs });
            }

            MatrixChart.Series = chartSeries;
            MatrixGrid.ItemsSource = tableRows;
            StyleChart(MatrixChart, "Размер матрицы n", $"Время, {unit}");
        }

        private void PowerRunButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PowerNMaxBox.Text, out int nMax) || nMax <= 0 ||
                !int.TryParse(PowerStepBox.Text, out int step) || step <= 0)
            {
                MessageBox.Show("Проверьте параметры N max и шаг.");
                return;
            }
            if (nMax > 5_000)
            {
                // Рекурсивный алгоритм уходит в рекурсию на глубину n.
                // При больших n стек переполнится и программа упадёт целиком (StackOverflow не ловится try/catch).
                MessageBox.Show("Для рекурсивного алгоритма глубина рекурсии равна n, поэтому N max ограничен 5 000 — иначе переполнится стек.");
                return;
            }

            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                int runId = _db.CreateRun("power", "Возведение в степень (шаги)", nMax, step, 1);
                var results = _powerBenchmark.RunExperiment(nMax, step, PowerUseCacheBox.IsChecked == true, runId);
                RenderPowerResults(results);
            }
            finally { Mouse.OverrideCursor = null; }
        }

        private void RenderPowerResults(List<PowerStepsPoint> results)
        {
            PowerGrid.ItemsSource = results;

            PowerChart.Series = new ISeries[]
            {
                new LineSeries<ObservablePoint>
                {
                    Values = results.Select(r => new ObservablePoint(r.N, r.SimpleSteps)).ToArray(),
                    Name = "Простой O(n)", GeometrySize = 0, Fill = null,
                    Stroke = new SolidColorPaint(Palette[0], 2)
                },
                new LineSeries<ObservablePoint>
                {
                    Values = results.Select(r => new ObservablePoint(r.N, r.RecursiveSteps)).ToArray(),
                    Name = "Рекурсивный O(n)", GeometrySize = 0, Fill = null,
                    Stroke = new SolidColorPaint(Palette[1], 2)
                },
                new LineSeries<ObservablePoint>
                {
                    Values = results.Select(r => new ObservablePoint(r.N, r.FastSteps)).ToArray(),
                    Name = "Быстрый бинарный O(log n)", GeometrySize = 0, Fill = null,
                    Stroke = new SolidColorPaint(Palette[2], 2)
                }
            };
        }

        private void LoadHistoryList()
        {
            var history = _db.GetHistory();
            HistoryList.ItemsSource = history.Select(h => new HistoryItemVm
            {
                Id = h.Id,
                Label = $"{h.AlgorithmName}  ·  n=1–{h.NMax}, шаг {h.Step}, {h.Runs} прог.  ·  {h.CreatedAt:dd.MM.yyyy HH:mm}"
            }).ToList();

            HistoryList.ItemTemplate = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(CheckBox));
            factory.SetBinding(CheckBox.ContentProperty, new System.Windows.Data.Binding("Label"));
            factory.SetBinding(CheckBox.IsCheckedProperty, new System.Windows.Data.Binding("IsChecked") { Mode = System.Windows.Data.BindingMode.TwoWay });
            factory.SetValue(MarginProperty, new Thickness(4));
            HistoryList.ItemTemplate.VisualTree = factory;
        }

        private void CompareButton_Click(object sender, RoutedEventArgs e)
        {
            if (HistoryList.ItemsSource is not IEnumerable<HistoryItemVm> items) return;
            var selected = items.Where(i => i.IsChecked).ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show("Отметьте хотя бы один эксперимент в списке слева.");
                return;
            }

            var chartSeries = new List<ISeries>();
            for (int i = 0; i < selected.Count; i++)
            {
                var runId = selected[i].Id;
                var points = _db.GetMeasurementsForRun(runId);
                var color = Palette[i % Palette.Length];

                chartSeries.Add(new LineSeries<ObservablePoint>
                {
                    Values = points.Select(p => new ObservablePoint(p.N, p.AvgTimeMs)).ToArray(),
                    Name = selected[i].Label,
                    Fill = null,
                    GeometrySize = 3,
                    Stroke = new SolidColorPaint(color, 2),
                    GeometryStroke = new SolidColorPaint(color, 2)
                });
            }

            HistoryChart.Series = chartSeries;
        }

        private static bool TryParsePositiveInts(out int a, out int b, out int c, string sa, string sb, string sc)
        {
            a = 0; b = 0; c = 0;

            if (!int.TryParse(sa, out int ta) || ta <= 0) return false;
            if (!int.TryParse(sb, out int tb) || tb <= 0) return false;
            if (!int.TryParse(sc, out int tc) || tc <= 0) return false;

            a = ta; b = tb; c = tc;
            return true;
        }
    }
}
