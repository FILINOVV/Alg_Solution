using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace LabApp.Ui
{
    // Одна "строка" поверхности — набор точек (X, Value) на фиксированной глубине Depth.
    // Для умножения матриц: Depth = m (число), Label = null — ось глубины размечается числами.
    // Для сравнения алгоритмов: Depth = просто порядковый номер алгоритма, Label = его имя —
    // ось глубины размечается этими именами, а не числами. Color — чтобы различать кривые,
    // когда их несколько (для матриц, где строка одна физическая величина, можно не указывать).
    public record SurfaceRow(double Depth, IReadOnlyList<(double X, double Value)> Points, Color? Color = null, string? Label = null);

    public partial class Chart3DView : UserControl
    {
        // Все данные вписываются в куб такого размера — так график всегда смотрится одинаково
        // аккуратно, независимо от реальных масштабов величин (n может быть миллионы, время — доли мс).
        private const double BoxSize = 10.0;

        // Расстояние между соседними кривыми в режиме samePlane (в тех же единицах, что и куб 10×10×10)
        private const double SamePlaneGap = 0.4;

        private readonly ModelVisual3D _sceneRoot = new();
        private Color _textColor = AppTheme.LightChart.Text;
        private Color _gridColor = AppTheme.LightChart.Separator;

        // Последний builder — чтобы перерисовать график при смене темы без пересчёта данных заново
        private Action? _redraw;

        // Вращение реализовано вручную (а не через встроенный контроллер камеры HelixToolkit),
        // чтобы оно 100% не зависело от того, как у конкретной версии библиотеки настроен хит-тест
        // мыши: крутим не камеру, а саму сцену — двумя поворотами вокруг центра куба.
        // Ориентация сцены хранится одной матрицей и крутится "трекболом": каждое движение мыши
        // добавляет поворот вокруг осей ЭКРАНА (вправо/влево — вокруг вертикали, вверх/вниз —
        // вокруг горизонтали). Никаких ограничений по углу — можно перевернуть куб как угодно.
        private Matrix3D _orient;
        private static readonly Point3D Center = new(BoxSize / 2, BoxSize / 2, BoxSize / 2);
        private bool _dragging;
        private Point _lastMousePos;
        private readonly MatrixTransform3D _sceneTransform = new();

        // Подписи осей — обычные 2D-TextBlock поверх 3D-вида (не 3D-биллборды). Для каждой
        // храним её "локальную" 3D-позицию (в системе координат сцены, ДО поворота), а экранное
        // место на канвасе пересчитываем каждый кадр: применяем текущий поворот сцены к этой
        // точке и проецируем через камеру. Так подписи — это нормальный текст пиксельного
        // размера, который не зависит от загадок размера/отсечения 3D-биллбордов HelixToolkit.
        private readonly List<(Point3D Local, TextBlock Element)> _labels = new();

        public bool HasContent => _sceneRoot.Children.Count > 0;

        public Chart3DView()
        {
            InitializeComponent();

            // Камера зафиксирована и смотрит прямо на центр куба; "вращение" — это поворот
            // самой сцены навстречу мыши, а не движение камеры
            // Угол обзора и ближняя/дальняя плоскости — пошире и подальше, чем могло бы хватить
            // впритык для самого куба. Подписи осей стоят ЗА пределами куба (TickOffset/TitleOffset),
            // а их реальный размер на экране зависит от FontSize — при более крупном FontSize
            // прямоугольник подписи становится больше и легко "вылезает" за пределы того, что
            // камера считает видимым, если запас по краям кадра мал. Раньше запаса почти не было
            // (под большие подписи), поэтому они то "съедались" (пропадали), то появлялись — решение
            // не в подборе ещё одного числа для FontSize, а в том, чтобы дать всей сцене больше
            // места в кадре.
            Viewport.Camera = CreateCamera();

            SetInitialOrientation();
            _sceneRoot.Transform = _sceneTransform;

            var lights = new Model3DGroup();
            lights.Children.Add(new AmbientLight(Color.FromRgb(140, 140, 140)));
            lights.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-1, -2, -1)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(90, 90, 90), new Vector3D(1, 1, 1)));
            // Свет крутим вместе со сценой, иначе при повороте одна сторона фигуры всегда
            // останется неосвещённой тёмной стороной
            Viewport.Children.Add(new ModelVisual3D { Content = lights, Transform = _sceneTransform });

            Viewport.Children.Add(_sceneRoot);

            Viewport.PreviewMouseLeftButtonDown += OnDragStart;
            Viewport.PreviewMouseMove += OnDragMove;
            Viewport.PreviewMouseLeftButtonUp += OnDragEnd;
            Viewport.MouseLeave += (_, _) => EndDrag();
            Viewport.SizeChanged += (_, _) => UpdateLabelPositions();

            // Экранные позиции 2D-подписей пересчитываем каждый кадр — это дёшево, а зависит
            // от камеры/поворота/масштаба (в т.ч. от встроенного зума и сдвига HelixToolkit,
            // за которыми мы отдельно не следим) так много всего, что проще обновлять всегда,
            // чем пытаться поймать каждое событие, которое может сдвинуть картинку.
            CompositionTarget.Rendering += (_, _) => UpdateLabelPositions();
        }

        private static PerspectiveCamera CreateCamera()
        {
            double c = BoxSize / 2;
            return new PerspectiveCamera
            {
                Position = new Point3D(c, c, c + BoxSize * 2.4),
                LookDirection = new Vector3D(0, 0, -1),
                UpDirection = new Vector3D(0, 1, 0),
                FieldOfView = 58,
                NearPlaneDistance = 0.1,
                FarPlaneDistance = 1000
            };
        }

        // Возвращает исходный вид: угол поворота и положение/масштаб камеры (если график
        // сдвинули или увеличили так, что оси ушли за край окошка)
        private void ResetView()
        {
            SetInitialOrientation();
            Viewport.Camera = CreateCamera();
        }

        private static Matrix3D RotationAround(Vector3D axis, double angle) =>
            new RotateTransform3D(new AxisAngleRotation3D(axis, angle), Center).Value;

        // Стартовый ракурс: чуть сверху и чуть сбоку
        private void SetInitialOrientation()
        {
            _orient = RotationAround(new Vector3D(1, 0, 0), 18) * RotationAround(new Vector3D(0, 1, 0), -28);
            _sceneTransform.Matrix = _orient;
        }

        // ================= Вращение мышью (ЛКМ-перетаскивание) =================

        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ResetView();
                e.Handled = true;
                return;
            }

            _dragging = true;
            _lastMousePos = e.GetPosition(Viewport);
            Viewport.CaptureMouse();
            e.Handled = true;
        }

        private void OnDragMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;

            var pos = e.GetPosition(Viewport);
            var dx = pos.X - _lastMousePos.X;
            var dy = pos.Y - _lastMousePos.Y;
            _lastMousePos = pos;

            // Новый поворот добавляется к текущему вокруг осей экрана — без ограничения по углу
            _orient = _orient
                * RotationAround(new Vector3D(0, 1, 0), dx * 0.4)
                * RotationAround(new Vector3D(1, 0, 0), dy * 0.4);
            _sceneTransform.Matrix = _orient;

            e.Handled = true;
        }

        private void OnDragEnd(object sender, MouseButtonEventArgs e) => EndDrag();

        private void EndDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            Viewport.ReleaseMouseCapture();
        }

        // Перекрашивает оси/сетку под текущую тему и перерисовывает последний показанный график
        public void ApplyTheme(ChartPalette palette)
        {
            _textColor = palette.Text;
            _gridColor = palette.Separator;
            _redraw?.Invoke();
        }

        // ================= Поверхность (матрицы) и прямые 3D-линии (сравнение алгоритмов) =================
        //
        // Два разных случая под одним методом:
        //  - "Чистая" числовая поверхность (матрицы, Label == null у всех строк) — одна гладкая
        //    сетка-поверхность по двум реальным параметрам (n и m), подписи по оси глубины — числа.
        //  - Сравнение нескольких алгоритмов (Label задан) — каждая кривая рисуется отдельно, прямой
        //    линией на своей глубине, без общей заливки между ними (соединять их в одну поверхность
        //    физически бессмысленно — "алгоритм 1.5" не существует). Подписи по оси глубины — просто
        //    номера 1, 2, 3…, а какому номеру какой алгоритм соответствует — смотрим в легенде.

        // samePlane = true — все кривые лежат В ОДНОЙ плоскости (на одной глубине). Нужно, когда
        // кривые надо сравнивать между собой по высоте (эксперимент против теории): при разной
        // глубине перспектива и поворот делают более дальнюю кривую визуально ниже/короче, и
        // можно "увидеть" что эксперимент лучше теории просто выбрав удачный угол. В одной
        // плоскости любой поворот искажает обе кривые одинаково.
        public void ShowSurface(IReadOnlyList<SurfaceRow> rows, string xTitle, string valueTitle, string depthTitle, bool samePlane = false)
        {
            _redraw = () => BuildSurface(rows, xTitle, valueTitle, depthTitle, samePlane);
            _redraw();
        }

        private void BuildSurface(IReadOnlyList<SurfaceRow> rows, string xTitle, string valueTitle, string depthTitle, bool samePlane)
        {
            _sceneRoot.Children.Clear();
            LegendPanel.Children.Clear();
            LabelCanvas.Children.Clear();
            _labels.Clear();

            var usableRows = rows.Where(r => r.Points.Count > 0).ToList();

            bool hasLabels = usableRows.Count > 0 && usableRows.All(r => r.Label != null);
            samePlane = samePlane && hasLabels;
            LegendBorder.Visibility = hasLabels ? Visibility.Visible : Visibility.Collapsed;
            if (hasLabels)
                foreach (var row in usableRows)
                    AddLegendItem(row.Label!, row.Color ?? _gridColor);

            if (usableRows.Count == 0) return;

            double xMin = usableRows.SelectMany(r => r.Points).Min(p => p.X);
            double xMax = usableRows.SelectMany(r => r.Points).Max(p => p.X);
            double zMin = usableRows.Min(r => r.Depth);
            double zMax = usableRows.Max(r => r.Depth);
            double yMax = usableRows.SelectMany(r => r.Points).Max(p => p.Value);
            const double yMin = 0;

            int count = usableRows.Count;
            double MapX(double x) => Norm(x, xMin, xMax) * BoxSize;
            double MapY(double y) => Norm(y, yMin, yMax) * BoxSize;
            // В режиме "одна плоскость" кривые всё же разведены по глубине на совсем малый шаг
            // (SamePlaneGap): иначе линии лежат ровно друг на друге и сливаются в одну.
            // Шаг очень мал, поэтому перспектива почти не искажает сравнение по высоте.
            double MapZ(int index, double depth) => samePlane
                ? BoxSize / 2 + (index - (count - 1) / 2.0) * SamePlaneGap
                : hasLabels
                    ? (count <= 1 ? BoxSize / 2 : index / (double)(count - 1) * BoxSize)
                    : Norm(depth, zMin, zMax) * BoxSize;

            if (hasLabels)
            {
                // Сравнение алгоритмов: каждая кривая — прямая линия на своей глубине + тонкая
                // полупрозрачная стенка от пола до значения, чтобы было видно высоту, а не только контур
                for (int i = 0; i < usableRows.Count; i++)
                {
                    var row = usableRows[i];
                    if (row.Points.Count < 2) continue;

                    double z = MapZ(i, row.Depth);
                    var color = row.Color ?? Colors.White;
                    var top = row.Points.Select(p => new Point3D(MapX(p.X), MapY(p.Value), z)).ToList();

                    _sceneRoot.Children.Add(new LinesVisual3D { Points = ToSegments(top), Color = color, Thickness = 2.2 });

                    double wallZ = z;
                    var flat = new List<Point3D>();
                    foreach (var p in top)
                    {
                        flat.Add(new Point3D(p.X, 0, wallZ));
                        flat.Add(new Point3D(p.X, p.Y, wallZ));
                    }
                    var mb = new MeshBuilder();
                    mb.AddRectangularMesh(flat, 2);
                    var wallBrush = new SolidColorBrush(Color.FromArgb(55, color.R, color.G, color.B));
                    _sceneRoot.Children.Add(new MeshGeometryVisual3D
                    {
                        MeshGeometry = mb.ToMesh(),
                        // Material задаём явно (только Diffuse), а не через Fill: Fill в HelixToolkit
                        // добавляет к материалу белый зеркальный слой (Specular) — это и был
                        // белый "блик от лампы", перекрывающий цвет стенки
                        Material = new DiffuseMaterial(wallBrush),
                        BackMaterial = new DiffuseMaterial(wallBrush)
                    });
                }
            }
            else
            {
                // Матрицы: одна гладкая поверхность по двум реальным параметрам
                int columns = usableRows[0].Points.Count;
                bool canBuildMesh = count >= 2 && usableRows.All(r => r.Points.Count == columns);

                if (canBuildMesh)
                {
                    var flat = new List<Point3D>();
                    for (int i = 0; i < usableRows.Count; i++)
                    {
                        var row = usableRows[i];
                        double z = MapZ(i, row.Depth);
                        foreach (var p in row.Points)
                            flat.Add(new Point3D(MapX(p.X), MapY(p.Value), z));
                    }

                    var mb = new MeshBuilder();
                    mb.AddRectangularMesh(flat, columns);
                    var surfaceBrush = new SolidColorBrush(Color.FromArgb(150, 59, 130, 246));
                    _sceneRoot.Children.Add(new MeshGeometryVisual3D
                    {
                        MeshGeometry = mb.ToMesh(),
                        Fill = surfaceBrush,
                        BackMaterial = new DiffuseMaterial(surfaceBrush)
                    });
                }

                foreach (var row in usableRows)
                {
                    double z = MapZ(0, row.Depth); // индекс тут не используется (hasLabels == false)
                    var line = row.Points.Select(p => new Point3D(MapX(p.X), MapY(p.Value), z)).ToList();
                    _sceneRoot.Children.Add(new LinesVisual3D { Points = ToSegments(line), Color = Colors.White, Thickness = 1 });
                }
            }

            DrawBoxFrame();
            AddXTicks(xMin, xMax, xTitle);
            AddYTicks(yMin, yMax, valueTitle);

            if (samePlane)
            {
                // Одна плоскость — оси глубины как таковой нет, подписывать нечего
            }
            else if (hasLabels)
            {
                // Подписи по оси глубины — просто номера кривых (1, 2, 3…), имена — в легенде,
                // иначе длинный текст алгоритмов толпится и наезжает на график
                var ticks = usableRows.Select((r, i) => (MapZ(i, r.Depth), (i + 1).ToString())).ToList();
                AddDepthTicksDiscrete(ticks, depthTitle);
            }
            else
            {
                AddDepthTicksNumeric(zMin, zMax, depthTitle);
            }
        }

        // ================= Экспорт в PNG =================

        // Снимок текущего вида (под тем углом камеры, который сейчас на экране)
        public void ExportPng(string path)
        {
            int width = Math.Max((int)ActualWidth, 1);
            int height = Math.Max((int)ActualHeight, 1);
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(this);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = new FileStream(path, FileMode.Create);
            encoder.Save(fs);
        }

        // ================= Вспомогательные построения: оси, сетка, подписи =================

        private void DrawBoxFrame()
        {
            var corners = new[]
            {
                new Point3D(0, 0, 0), new Point3D(BoxSize, 0, 0), new Point3D(BoxSize, 0, BoxSize), new Point3D(0, 0, BoxSize),
                new Point3D(0, BoxSize, 0), new Point3D(BoxSize, BoxSize, 0), new Point3D(BoxSize, BoxSize, BoxSize), new Point3D(0, BoxSize, BoxSize)
            };
            int[,] edges =
            {
                { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 },
                { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 },
                { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 }
            };

            var pts = new Point3DCollection();
            for (int i = 0; i < edges.GetLength(0); i++)
            {
                pts.Add(corners[edges[i, 0]]);
                pts.Add(corners[edges[i, 1]]);
            }

            _sceneRoot.Children.Add(new LinesVisual3D { Points = pts, Color = _gridColor, Thickness = 1 });
        }

        // 4 деления вместо 6 — меньше текста толпится около осей
        private const int TickSteps = 4;

        // Название оси (не цифры делений, а подпись типа "Размер вектора n") стоит у конца оси,
        // как принято на обычных графиках — а не висит посередине, где мешает самим данным.
        // Отступ от куба — умеренный: чем дальше подписи "торчат" за пределы куба, тем ближе
        // они к краю кадра камеры, и тем легче им не влезть, когда текст покрупнее.
        private const double TickOffset = 1.1;
        private const double TitleOffset = 1.0;

        private void AddXTicks(double min, double max, string title)
        {
            for (int i = 0; i <= TickSteps; i++)
            {
                double t = i / (double)TickSteps;
                AddTickLabel(new Point3D(t * BoxSize, -TickOffset, 0), FormatTick(min + t * (max - min)));
            }
            AddAxisTitle(new Point3D(BoxSize + TitleOffset, -TickOffset, 0), title);
        }

        private void AddYTicks(double min, double max, string title)
        {
            for (int i = 0; i <= TickSteps; i++)
            {
                double t = i / (double)TickSteps;
                AddTickLabel(new Point3D(-TickOffset, t * BoxSize, 0), FormatTick(min + t * (max - min)));
            }
            AddAxisTitle(new Point3D(-TickOffset, BoxSize + TitleOffset, 0), title);
        }

        private void AddDepthTicksNumeric(double min, double max, string title)
        {
            for (int i = 0; i <= TickSteps; i++)
            {
                double t = i / (double)TickSteps;
                AddTickLabel(new Point3D(0, -TickOffset, t * BoxSize), FormatTick(min + t * (max - min)));
            }
            AddAxisTitle(new Point3D(0, -TickOffset, BoxSize + TitleOffset), title);
        }

        private void AddDepthTicksDiscrete(IReadOnlyList<(double Z, string Label)> ticks, string title)
        {
            double lastZ = BoxSize / 2;
            foreach (var (z, label) in ticks)
            {
                AddTickLabel(new Point3D(0, -TickOffset, z), label);
                lastZ = Math.Max(lastZ, z);
            }
            AddAxisTitle(new Point3D(0, -TickOffset, lastZ + TitleOffset), title);
        }

        // Подписи осей — обычный 2D-текст (см. UpdateLabelPositions), размер — нормальные
        // пиксели, как у любого WPF-текста, никаких загадок с мировыми единицами или
        // отсечением камерой.
        private const double TickFontSize = 11;
        private const double TitleFontSize = 13;

        private void AddTickLabel(Point3D position, string text) => AddLabel(position, text, TickFontSize, FontWeights.Normal);

        private void AddAxisTitle(Point3D position, string text) => AddLabel(position, text, TitleFontSize, FontWeights.Bold);

        private void AddLabel(Point3D localPosition, string text, double fontSize, FontWeight weight)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = weight,
                Foreground = new SolidColorBrush(_textColor)
            };
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            LabelCanvas.Children.Add(block);
            _labels.Add((localPosition, block));
        }

        // Пересчитывает экранные (2D) координаты всех подписей из их 3D-позиций: применяем
        // текущий поворот сцены к точке и проецируем через камеру тем же способом, которым
        // HelixToolkit сам превращает 3D-координаты в пиксели окна.
        private void UpdateLabelPositions()
        {
            if (_labels.Count == 0) return;

            foreach (var (local, block) in _labels)
            {
                var world = _sceneTransform.Transform(local);
                // Point3DtoPoint2D (именно так, с маленькой "to") — не Point3DToPoint2D и не
                // расширяющий метод: статический, принимает viewport первым параметром,
                // возвращает Point (не Point?).
                Point p = Viewport3DHelper.Point3DtoPoint2D(Viewport.Viewport, world);
                if (!double.IsNaN(p.X) && !double.IsNaN(p.Y) && !double.IsInfinity(p.X) && !double.IsInfinity(p.Y))
                {
                    block.Visibility = Visibility.Visible;
                    Canvas.SetLeft(block, p.X - block.DesiredSize.Width / 2);
                    Canvas.SetTop(block, p.Y - block.DesiredSize.Height / 2);
                }
                else
                {
                    block.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void AddLegendItem(string name, Color color)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new Border
            {
                Width = 12, Height = 12, Background = new SolidColorBrush(color),
                CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center
            });
            row.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 12 });
            LegendPanel.Children.Add(row);
        }

        private static Point3DCollection ToSegments(IReadOnlyList<Point3D> pts)
        {
            var result = new Point3DCollection();
            for (int i = 0; i < pts.Count - 1; i++)
            {
                result.Add(pts[i]);
                result.Add(pts[i + 1]);
            }
            return result;
        }

        private static double Norm(double v, double min, double max) => max > min ? (v - min) / (max - min) : 0.5;

        // 2000000 -> «2 000 000», 0.00012 -> «0,00012», 12.3456 -> «12,3»
        private static string FormatTick(double value) =>
            Math.Abs(value) >= 1000 ? value.ToString("N0") : value.ToString("G3");
    }
}
