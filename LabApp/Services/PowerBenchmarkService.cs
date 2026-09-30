using LabApp.Algorithms;
using LabApp.Models;

namespace LabApp.Services
{
    public class PowerStepsPoint
    {
        public int N { get; set; }
        public int SimpleSteps { get; set; }
        public int RecursiveSteps { get; set; }
        public int FastSteps { get; set; }
    }

    public class PowerBenchmarkService
    {
        private readonly DatabaseService _db;

        public PowerBenchmarkService(DatabaseService db) => _db = db;

        public List<PowerStepsPoint> RunExperiment(int nMax, int step, bool useCache, int runId)
        {
            const double x = 1.0001;
            var result = new List<PowerStepsPoint>();

            for (int n = step; n <= nMax; n += step)
            {
                int simple = GetOrCompute("pow_simple", n, useCache, runId,
                    () => PowerAlgorithms.SimpleIterativeSteps(x, n));

                int recursive = GetOrCompute("pow_recursive", n, useCache, runId,
                    () => PowerAlgorithms.RecursiveSteps(x, n));

                int fast = GetOrCompute("pow_fast", n, useCache, runId,
                    () => PowerAlgorithms.FastBinarySteps(x, n));

                result.Add(new PowerStepsPoint { N = n, SimpleSteps = simple, RecursiveSteps = recursive, FastSteps = fast });
            }

            return result;
        }

        private int GetOrCompute(string key, int n, bool useCache, int runId, Func<int> compute)
        {
            var cached = useCache ? _db.TryGetCached(key, n, 1) : null;
            int steps;
            if (cached != null)
            {
                steps = (int)cached.AvgTimeMs;
            }
            else
            {
                steps = compute();
                _db.SaveMeasurement(runId, key, new ExperimentResult
                {
                    N = n, AvgTimeMs = steps, StdDevMs = 0, Runs = 1
                });
            }
            return steps;
        }
    }
}
