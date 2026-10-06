using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LabApp.Algorithms;
using LabApp.Models;
using LabApp.Services;
using LabApp.Ui;

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

        // Цвета серий подобраны так, чтобы читаться и на светлом, и на тёмном фоне
        private static readonly Color[] Palette =
        {
            Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0xF9, 0x73, 0x16), Color.FromRgb(0x10, 0xB9, 0x81),
            Color.FromRgb(0xA8, 0x55, 0xF7), Color.FromRgb(0xEA, 0xB3, 0x08), Color.FromRgb(0xEF, 0x44, 0x44)
        };

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

            ResultChart.ApplyTheme(AppTheme.Chart);
            MatrixChart.ApplyTheme(AppTheme.Chart);
            PowerChart.ApplyTheme(AppTheme.Chart);
            HistoryChart.ApplyTheme(AppTheme.Chart);
            UpdateThemeButton();
        }

        // ================= Тема =================

        private void ThemeToggle_Click(object sender, RoutedEventArgs e) => AppTheme.Toggle();

        private void OnThemeChanged()
        {
            ResultChart.ApplyTheme(AppTheme.Chart);
            MatrixChart.ApplyTheme(AppTheme.Chart);
            PowerChart.ApplyTheme(AppTheme.Chart);
            HistoryChart.ApplyTheme(AppTheme.Chart);
            UpdateThemeButton();
        }

        private void UpdateThemeButton()
        {
            // Иконки из шрифта Segoe MDL2 Assets: E706 — солнце, E708 — луна
            ThemeIcon.Text = AppTheme.IsDark ? "" : "";
            ThemeLabel.Text = AppTheme.IsDark ? "Светлая тема" : "Тёмная тема";
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
            if (sender is not FrameworkElement { Tag: string chartName } || FindName(chartName) is not Chart3DView chart)
                return;

            if (!chart.HasContent)
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

            try
            {
                chart.ExportPng(dialog.FileName);
                MessageBox.Show($"График сохранён (текущий ракурс камеры):\n{dialog.FileName}");
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

            var empirical = results.Select(r => ((double)r.N, r.AvgTimeMs * factor)).ToArray();
            var theoretical = results
                .Select(r => ((double)r.N, c * BenchmarkService.TheoreticalF(algo.Complexity, r.N) * factor))
                .ToArray();

            ResultChart.ShowSurface(new[]
            {
                new SurfaceRow(0, empirical, Palette[0], "Эксперимент (среднее)"),
                new SurfaceRow(1, theoretical, Palette[1], $"Теория {algo.ComplexityLabel}")
            }, "Размер вектора n", $"Время, {unit}", "Серия");
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

            double maxMs = series.SelectMany(ser => ser.Points).Select(pt => pt.AvgTimeMs).DefaultIfEmpty(0).Max();
            var (factor, unit) = PickTimeUnit(maxMs);

            var rows = new List<SurfaceRow>();
            foreach (var s in series)
            {
                var points = s.Points.Select(p => ((double)p.N, p.AvgTimeMs * factor)).ToArray();
                rows.Add(new SurfaceRow(s.M, points));

                foreach (var p in s.Points)
                    tableRows.Add(new { p.N, M = s.M, p.AvgTimeMs });
            }

            // Настоящая 3D-поверхность: X = n, глубина (Z) = m, высота (Y) = время —
            // в отличие от остальных графиков тут обе оси нижнего уровня — реальные параметры эксперимента
            MatrixChart.ShowSurface(rows, "Размер матрицы n", $"Время, {unit}", "m");
            MatrixGrid.ItemsSource = tableRows;
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

            PowerChart.ShowSurface(new[]
            {
                new SurfaceRow(0, results.Select(r => ((double)r.N, (double)r.SimpleSteps)).ToArray(), Palette[0], "Простой O(n)"),
                new SurfaceRow(1, results.Select(r => ((double)r.N, (double)r.RecursiveSteps)).ToArray(), Palette[1], "Рекурсивный O(n)"),
                new SurfaceRow(2, results.Select(r => ((double)r.N, (double)r.FastSteps)).ToArray(), Palette[2], "Быстрый бинарный O(log n)")
            }, "Показатель степени n", "Количество умножений", "Алгоритм");
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

            var rows = new List<SurfaceRow>();
            for (int i = 0; i < selected.Count; i++)
            {
                var points = _db.GetMeasurementsForRun(selected[i].Id);
                rows.Add(new SurfaceRow(
                    i,
                    points.Select(p => ((double)p.N, p.AvgTimeMs)).ToArray(),
                    Palette[i % Palette.Length],
                    selected[i].Label));
            }

            // Если у сравниваемых экспериментов разное число точек (разные n max/шаг) —
            // соединяющая поверхность сама не построится, останутся только сами кривые
            HistoryChart.ShowSurface(rows, "n", "Время, мс (для степеней — умножения)", "Эксперимент");
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
