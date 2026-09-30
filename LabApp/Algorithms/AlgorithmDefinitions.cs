namespace LabApp.Algorithms
{
    public class AlgorithmDefinition
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string ComplexityLabel { get; set; } = "";
        public string Description { get; set; } = "";
        public ComplexityKind Complexity { get; set; }

        // Подготовка входных данных (генерация вектора). В замер времени НЕ входит.
        public Func<int, Random, double[]> Prepare { get; set; } = AlgorithmCatalog.GenerateVector;

        // Сам алгоритм — только эта часть попадает под секундомер.
        // Возвращает число, чтобы оптимизатор не выбросил "бесполезные" вычисления.
        public Func<double[], double> Run { get; set; } = _ => 0;

        // true — алгоритм меняет массив (все сортировки). Такой алгоритм нельзя
        // запускать повторно на тех же данных: во второй раз массив уже отсортирован.
        public bool MutatesInput { get; set; }
    }

    public enum ComplexityKind { Const, Linear, NLogN, Quadratic, Cubic, Log }

    public static class AlgorithmCatalog
    {
        private const int TimsortMinRun = 32;

        public static int RecommendedNMax(ComplexityKind kind) => kind switch
        {
            ComplexityKind.Const => 2_000_000,
            ComplexityKind.Linear => 2_000_000,
            ComplexityKind.Log => 5_000_000,
            ComplexityKind.NLogN => 200_000,
            ComplexityKind.Quadratic => 8_000,
            ComplexityKind.Cubic => 300,
            _ => 10_000
        };

        public static List<AlgorithmDefinition> All => new()
        {
            new AlgorithmDefinition
            {
                Key = "const",
                Name = "Постоянная функция f(v) = 1",
                ComplexityLabel = "O(1)",
                Complexity = ComplexityKind.Const,
                Description = "Возвращает 1 независимо от размера вектора.",
                Run = v => 1
            },
            new AlgorithmDefinition
            {
                Key = "sum",
                Name = "Сумма элементов вектора",
                ComplexityLabel = "O(n)",
                Complexity = ComplexityKind.Linear,
                Description = "Вычисление суммы элементов n-мерного вектора.",
                Run = v =>
                {
                    double sum = 0;
                    for (int i = 0; i < v.Length; i++) sum += v[i];
                    return sum;
                }
            },
            new AlgorithmDefinition
            {
                Key = "product",
                Name = "Произведение элементов",
                ComplexityLabel = "O(n)",
                Complexity = ComplexityKind.Linear,
                Description = "Вычисление произведения элементов n-мерного вектора.",
                Run = v =>
                {
                    double product = 1;
                    for (int i = 0; i < v.Length; i++) product *= v[i];
                    return product;
                }
            },
            new AlgorithmDefinition
            {
                Key = "poly_naive",
                Name = "Многочлен: наивное вычисление",
                ComplexityLabel = "O(n²)",
                Complexity = ComplexityKind.Quadratic,
                Description = "P(x) при x=1.5, степень x^(k-1) считается заново для каждого члена.",
                Run = v =>
                {
                    double x = 1.5, result = 0;
                    for (int k = 0; k < v.Length; k++)
                    {
                        double xp = 1;
                        for (int j = 0; j < k; j++) xp *= x;
                        result += v[k] * xp;
                    }
                    return result;
                }
            },
            new AlgorithmDefinition
            {
                Key = "poly_horner",
                Name = "Многочлен: метод Горнера",
                ComplexityLabel = "O(n)",
                Complexity = ComplexityKind.Linear,
                Description = "P(x) при x=1.5, вычисление по схеме Горнера.",
                Run = v =>
                {
                    double x = 1.5, result = 0;
                    for (int k = v.Length - 1; k >= 0; k--) result = result * x + v[k];
                    return result;
                }
            },
            new AlgorithmDefinition
            {
                Key = "bubble",
                Name = "Сортировка пузырьком",
                ComplexityLabel = "O(n²)",
                Complexity = ComplexityKind.Quadratic,
                Description = "Классическая сортировка пузырьком элементов вектора.",
                MutatesInput = true,
                Run = v =>
                {
                    for (int i = 0; i < v.Length - 1; i++)
                        for (int j = 0; j < v.Length - i - 1; j++)
                            if (v[j] > v[j + 1]) (v[j], v[j + 1]) = (v[j + 1], v[j]);
                    return 0;
                }
            },
            new AlgorithmDefinition
            {
                Key = "quicksort",
                Name = "Quick sort",
                ComplexityLabel = "O(n log n)",
                Complexity = ComplexityKind.NLogN,
                Description = "Рекурсивная сортировка с разбиением по опорному элементу.",
                MutatesInput = true,
                Run = v =>
                {
                    QuickSort(v, 0, v.Length - 1);
                    return 0;
                }
            },
            new AlgorithmDefinition
            {
                Key = "timsort",
                Name = "Timsort",
                ComplexityLabel = "O(n log n)",
                Complexity = ComplexityKind.NLogN,
                Description = "Упрощённый Timsort: массив режется на куски по 32 элемента, каждый кусок " +
                              "сортируется вставками, затем куски попарно сливаются, как в сортировке слиянием. " +
                              "(Встроенный Array.Sort в .NET — это не Timsort, а IntroSort, поэтому Timsort написан вручную.)",
                MutatesInput = true,
                Run = v =>
                {
                    TimSort(v);
                    return 0;
                }
            },
            new AlgorithmDefinition
            {
                Key = "individual_linear_search",
                Name = "Линейный поиск элемента в массиве",
                ComplexityLabel = "O(n)",
                Complexity = ComplexityKind.Linear,
                Description = "Индивидуальное задание: последовательный перебор элементов массива в поисках заданного значения. " +
                               "Для честного замера ищем заведомо отсутствующее значение — тогда алгоритм всегда " +
                               "просматривает весь массив (это и есть худший случай O(n)).",
                Run = v =>
                {
                    const double target = -1;
                    for (int i = 0; i < v.Length; i++)
                    {
                        if (v[i] == target) return i;
                    }
                    return -1;
                }
            },
            new AlgorithmDefinition
            {
                Key = "heapsort",
                Name = "Пирамидальная сортировка (Heap sort)",
                ComplexityLabel = "O(n log n)",
                Complexity = ComplexityKind.NLogN,
                Description = "Сортировка на основе структуры двоичной кучи: сначала строится куча, затем поочерёдно извлекается максимум.",
                MutatesInput = true,
                Run = v =>
                {
                    HeapSort(v);
                    return 0;
                }
            },
            new AlgorithmDefinition
            {
                Key = "binary_search",
                Name = "Бинарный поиск",
                ComplexityLabel = "O(log n)",
                Complexity = ComplexityKind.Log,
                Description = "Поиск элемента в отсортированном массиве делением диапазона поиска пополам на каждом шаге.",
                Prepare = GenerateSortedVector,
                Run = v => BinarySearch(v, -1)
            },
        };

        internal static double[] GenerateVector(int n, Random rnd)
        {
            var v = new double[n];
            for (int i = 0; i < n; i++) v[i] = rnd.NextDouble() * 100;
            return v;
        }

        internal static double[] GenerateSortedVector(int n, Random rnd)
        {
            var v = new double[n];
            double cur = 0;
            for (int i = 0; i < n; i++)
            {
                cur += rnd.NextDouble() * 10;
                v[i] = cur;
            }
            return v;
        }

        private static void HeapSort(double[] a)
        {
            int n = a.Length;
            for (int i = n / 2 - 1; i >= 0; i--) Heapify(a, n, i);
            for (int i = n - 1; i > 0; i--)
            {
                (a[0], a[i]) = (a[i], a[0]);
                Heapify(a, i, 0);
            }
        }

        private static void Heapify(double[] a, int n, int i)
        {
            int largest = i, left = 2 * i + 1, right = 2 * i + 2;
            if (left < n && a[left] > a[largest]) largest = left;
            if (right < n && a[right] > a[largest]) largest = right;
            if (largest != i)
            {
                (a[i], a[largest]) = (a[largest], a[i]);
                Heapify(a, n, largest);
            }
        }

        private static int BinarySearch(double[] a, double target)
        {
            int lo = 0, hi = a.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (a[mid] == target) return mid;
                if (a[mid] < target) lo = mid + 1;
                else hi = mid - 1;
            }
            return -1;
        }

        private static void QuickSort(double[] a, int lo, int hi)
        {
            if (lo >= hi) return;
            double pivot = a[(lo + hi) / 2];
            int i = lo, j = hi;
            while (i <= j)
            {
                while (a[i] < pivot) i++;
                while (a[j] > pivot) j--;
                if (i <= j) { (a[i], a[j]) = (a[j], a[i]); i++; j--; }
            }
            QuickSort(a, lo, j);
            QuickSort(a, i, hi);
        }

        // ===== Timsort (упрощённый) =====

        private static void TimSort(double[] a)
        {
            int n = a.Length;

            // Шаг 1. Режем массив на куски по 32 элемента и сортируем каждый вставками
            for (int start = 0; start < n; start += TimsortMinRun)
                InsertionSort(a, start, Math.Min(start + TimsortMinRun - 1, n - 1));

            // Шаг 2. Сливаем соседние куски попарно: 32+32 -> 64, 64+64 -> 128 и т.д.
            var buffer = new double[n];
            for (int size = TimsortMinRun; size < n; size *= 2)
            {
                for (int left = 0; left < n - size; left += 2 * size)
                {
                    int mid = left + size - 1;
                    int right = Math.Min(left + 2 * size - 1, n - 1);
                    Merge(a, buffer, left, mid, right);
                }
            }
        }

        private static void InsertionSort(double[] a, int left, int right)
        {
            for (int i = left + 1; i <= right; i++)
            {
                double key = a[i];
                int j = i - 1;
                while (j >= left && a[j] > key)
                {
                    a[j + 1] = a[j];
                    j--;
                }
                a[j + 1] = key;
            }
        }

        private static void Merge(double[] a, double[] buffer, int left, int mid, int right)
        {
            if (a[mid] <= a[mid + 1]) return; // куски уже стоят по порядку — сливать нечего

            Array.Copy(a, left, buffer, left, right - left + 1);
            int i = left, j = mid + 1, k = left;
            while (i <= mid && j <= right)
                a[k++] = buffer[i] <= buffer[j] ? buffer[i++] : buffer[j++];
            while (i <= mid) a[k++] = buffer[i++];
            while (j <= right) a[k++] = buffer[j++];
        }
    }
}
