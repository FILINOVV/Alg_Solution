using System.IO;
using Microsoft.Data.Sqlite;
using LabApp.Models;

namespace LabApp.Services
{
    public class DatabaseService
    {
        private readonly string _connectionString;

        public DatabaseService()
        {
            // v2: замеры после исправления методики. Старый results.db хранит неверные
            // значения в кэше, поэтому используем новый файл, чтобы кэш их не подтянул.
            string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "results_v2.db");
            _connectionString = $"Data Source={dbPath}";
            Initialize();
        }

        private SqliteConnection GetConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }

        private void Initialize()
        {
            using var conn = GetConnection();

            using var cmdRuns = conn.CreateCommand();
            cmdRuns.CommandText = @"
                CREATE TABLE IF NOT EXISTS experiment_runs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    algorithm_key TEXT NOT NULL,
                    algorithm_name TEXT NOT NULL,
                    n_max INTEGER NOT NULL,
                    step INTEGER NOT NULL,
                    runs INTEGER NOT NULL,
                    created_at TEXT NOT NULL
                );";
            cmdRuns.ExecuteNonQuery();

            using var cmdPoints = conn.CreateCommand();
            cmdPoints.CommandText = @"
                CREATE TABLE IF NOT EXISTS measurements (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    run_id INTEGER NOT NULL,
                    algorithm_key TEXT NOT NULL,
                    n INTEGER NOT NULL,
                    avg_time_ms REAL NOT NULL,
                    std_dev_ms REAL NOT NULL,
                    runs INTEGER NOT NULL,
                    FOREIGN KEY(run_id) REFERENCES experiment_runs(id)
                );";
            cmdPoints.ExecuteNonQuery();

            using var cmdIndex = conn.CreateCommand();
            cmdIndex.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_cache
                ON measurements(algorithm_key, n, runs);";
            cmdIndex.ExecuteNonQuery();
        }

        public ExperimentResult? TryGetCached(string algorithmKey, int n, int runs)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT avg_time_ms, std_dev_ms, runs FROM measurements
                WHERE algorithm_key = $key AND n = $n AND runs = $runs
                ORDER BY id DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("$key", algorithmKey);
            cmd.Parameters.AddWithValue("$n", n);
            cmd.Parameters.AddWithValue("$runs", runs);

            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new ExperimentResult
                {
                    N = n,
                    AvgTimeMs = reader.GetDouble(0),
                    StdDevMs = reader.GetDouble(1),
                    Runs = reader.GetInt32(2)
                };
            }
            return null;
        }

        public int CreateRun(string algorithmKey, string algorithmName, int nMax, int step, int runs)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO experiment_runs (algorithm_key, algorithm_name, n_max, step, runs, created_at)
                VALUES ($key, $name, $nmax, $step, $runs, $created);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$key", algorithmKey);
            cmd.Parameters.AddWithValue("$name", algorithmName);
            cmd.Parameters.AddWithValue("$nmax", nMax);
            cmd.Parameters.AddWithValue("$step", step);
            cmd.Parameters.AddWithValue("$runs", runs);
            cmd.Parameters.AddWithValue("$created", DateTime.Now.ToString("s"));

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void SaveMeasurement(int runId, string algorithmKey, ExperimentResult result)
        {
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO measurements (run_id, algorithm_key, n, avg_time_ms, std_dev_ms, runs)
                VALUES ($runId, $key, $n, $avg, $std, $runs);";
            cmd.Parameters.AddWithValue("$runId", runId);
            cmd.Parameters.AddWithValue("$key", algorithmKey);
            cmd.Parameters.AddWithValue("$n", result.N);
            cmd.Parameters.AddWithValue("$avg", result.AvgTimeMs);
            cmd.Parameters.AddWithValue("$std", result.StdDevMs);
            cmd.Parameters.AddWithValue("$runs", result.Runs);
            cmd.ExecuteNonQuery();
        }

        public List<ExperimentRun> GetHistory()
        {
            var list = new List<ExperimentRun>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, algorithm_key, algorithm_name, n_max, step, runs, created_at
                FROM experiment_runs ORDER BY id DESC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ExperimentRun
                {
                    Id = reader.GetInt32(0),
                    AlgorithmKey = reader.GetString(1),
                    AlgorithmName = reader.GetString(2),
                    NMax = reader.GetInt32(3),
                    Step = reader.GetInt32(4),
                    Runs = reader.GetInt32(5),
                    CreatedAt = DateTime.Parse(reader.GetString(6))
                });
            }
            return list;
        }

        public List<ExperimentResult> GetMeasurementsForRun(int runId)
        {
            var list = new List<ExperimentResult>();
            using var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT n, avg_time_ms, std_dev_ms, runs FROM measurements
                WHERE run_id = $runId ORDER BY n ASC;";
            cmd.Parameters.AddWithValue("$runId", runId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ExperimentResult
                {
                    N = reader.GetInt32(0),
                    AvgTimeMs = reader.GetDouble(1),
                    StdDevMs = reader.GetDouble(2),
                    Runs = reader.GetInt32(3)
                });
            }
            return list;
        }
    }
}
