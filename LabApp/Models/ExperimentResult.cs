namespace LabApp.Models
{
    public class ExperimentResult
    {
        public int N { get; set; }
        public double AvgTimeMs { get; set; }
        public double StdDevMs { get; set; }
        public int Runs { get; set; }
    }

    public class ExperimentRun
    {
        public int Id { get; set; }
        public string AlgorithmKey { get; set; } = "";
        public string AlgorithmName { get; set; } = "";
        public int NMax { get; set; }
        public int Step { get; set; }
        public int Runs { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
