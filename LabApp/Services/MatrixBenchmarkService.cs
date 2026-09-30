using System.Diagnostics;
using LabApp.Algorithms;
using LabApp.Models;

namespace LabApp.Services
{
    public class MatrixSeriesResult
    {
        public int M { get; set; }
        public List<ExperimentResult> Points { get; set; } = new();
    }

    public class MatrixBenchmarkService
    {
        private readonly DatabaseService _db;
        private readonly Random _rnd = new();

        public MatrixBenchmarkService(DatabaseService db) => _db = db;

        public List<MatrixSeriesResult> RunExperiment(
            int nMax, int step, int runs, bool useCache, int[] mValues, int runId)
        {
            var series = new List<MatrixSeriesResult>();

            foreach (int m in mValues)
            {
                string key = $"matrix_m{m}";
                var points = new List<ExperimentResult>();

                for (int n = step; n <= nMax; n += step)
                {
                    var cached = useCache ? _db.TryGetCached(key, n, runs) : null;
                    ExperimentResult point;

                    if (cached != null)
                    {
                        point = cached;
                    }
                    else
                    {
                        var times = new double[runs];
                        for (int r = 0; r < runs; r++)
                        {
                            // Генерация матриц — до запуска секундомера, в замер не входит
                            var a = MatrixAlgorithm.GenerateMatrix(n, m, _rnd);
                            var b = MatrixAlgorithm.GenerateMatrix(m, n, _rnd);

                            var sw = Stopwatch.StartNew();
                            _ = MatrixAlgorithm.Multiply(a, b);
                            sw.Stop();
                            times[r] = sw.Elapsed.TotalMilliseconds;
                        }
                        double avg = times.Average();
                        double std = Math.Sqrt(times.Select(t => (t - avg) * (t - avg)).Sum() / runs);
                        point = new ExperimentResult { N = n, AvgTimeMs = avg, StdDevMs = std, Runs = runs };
                    }

                    _db.SaveMeasurement(runId, key, point);
                    points.Add(point);
                }

                series.Add(new MatrixSeriesResult { M = m, Points = points });
            }

            return series;
        }
    }
}
