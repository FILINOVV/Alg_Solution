using System.Diagnostics;
using LabApp.Algorithms;
using LabApp.Models;

namespace LabApp.Services
{
    public class BenchmarkService
    {
        // Минимальная длительность одного замера. Если алгоритм работает быстрее
        // (O(1), O(log n), маленькие n), запускаем его много раз подряд
        // и делим общее время на число повторов. Иначе Stopwatch просто не успевает
        // "увидеть" такие короткие промежутки и на графике получается шум.
        private const double MinMeasureMs = 5.0;

        // "Копилка" для результатов алгоритмов: не даёт оптимизатору выбросить вычисления.
        private static double _sink;
        public static double Sink => _sink;

        private readonly DatabaseService _db;
        private readonly Random _rnd = new();

        public BenchmarkService(DatabaseService db) => _db = db;

        public List<ExperimentResult> RunExperiment(
            AlgorithmDefinition algo, int nMax, int step, int runs,
            bool useCache, int runId)
        {
            var results = new List<ExperimentResult>();

            for (int n = step; n <= nMax; n += step)
            {
                ExperimentResult? cached = useCache ? _db.TryGetCached(algo.Key, n, runs) : null;

                ExperimentResult point;
                if (cached != null)
                {
                    point = cached;
                }
                else
                {
                    var times = new double[runs];
                    for (int r = 0; r < runs; r++)
                        times[r] = MeasureOnce(algo, n);

                    double avg = times.Average();
                    double variance = times.Select(t => (t - avg) * (t - avg)).Sum() / runs;
                    double std = Math.Sqrt(variance);

                    point = new ExperimentResult { N = n, AvgTimeMs = avg, StdDevMs = std, Runs = runs };
                }

                results.Add(point);
            }

            // Все точки — в базу одной транзакцией, а не по одной на каждом шаге
            _db.SaveMeasurements(runId, algo.Key, results);

            return results;
        }

        // Один замер: сколько миллисекунд занимает ОДИН запуск алгоритма на векторе длины n
        private double MeasureOnce(AlgorithmDefinition algo, int n)
        {
            // 1. Готовим данные. Секундомер ещё не запущен — генерация в замер не попадает
            double[] data = algo.Prepare(n, _rnd);

            // 2. Сортировки портят массив, поэтому для них — ровно один запуск
            if (algo.MutatesInput)
            {
                var sw = Stopwatch.StartNew();
                _sink += algo.Run(data);
                sw.Stop();
                return sw.Elapsed.TotalMilliseconds;
            }

            // 3. Остальные запускаем пачками 1, 2, 4, 8... пока суммарно не набежит MinMeasureMs
            long repeats = 0;
            int batch = 1;
            var timer = Stopwatch.StartNew();
            while (true)
            {
                for (int i = 0; i < batch; i++) _sink += algo.Run(data);
                repeats += batch;

                if (timer.Elapsed.TotalMilliseconds >= MinMeasureMs) break;
                if (batch < (1 << 20)) batch *= 2;
            }
            timer.Stop();

            return timer.Elapsed.TotalMilliseconds / repeats;
        }

        public static double TheoreticalF(ComplexityKind kind, int n) => kind switch
        {
            ComplexityKind.Const => 1,
            ComplexityKind.Linear => n,
            ComplexityKind.Log => Math.Log2(Math.Max(n, 1)),
            ComplexityKind.NLogN => n * Math.Log2(Math.Max(n, 1)),
            ComplexityKind.Quadratic => (double)n * n,
            ComplexityKind.Cubic => (double)n * n * n,
            _ => n
        };

        public static (double C, double Mse) Approximate(List<ExperimentResult> data, ComplexityKind kind)
        {
            double sumFy = 0, sumFF = 0;
            foreach (var p in data)
            {
                double f = TheoreticalF(kind, p.N);
                sumFy += f * p.AvgTimeMs;
                sumFF += f * f;
            }
            double c = sumFF > 0 ? sumFy / sumFF : 0;

            double mse = 0;
            foreach (var p in data)
            {
                double approx = c * TheoreticalF(kind, p.N);
                mse += (p.AvgTimeMs - approx) * (p.AvgTimeMs - approx);
            }
            mse /= Math.Max(data.Count, 1);

            return (c, mse);
        }
    }
}
