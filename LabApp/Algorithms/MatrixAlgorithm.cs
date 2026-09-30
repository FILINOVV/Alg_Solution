namespace LabApp.Algorithms
{
    public static class MatrixAlgorithm
    {
        // Классическое умножение: A(n×m) × B(m×n) = C(n×n). Три вложенных цикла -> O(n²·m)
        public static double[,] Multiply(double[,] a, double[,] b)
        {
            int n = a.GetLength(0);
            int m = a.GetLength(1);
            var c = new double[n, n];

            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < m; k++)
                        sum += a[i, k] * b[k, j];
                    c[i, j] = sum;
                }
            }
            return c;
        }

        public static double TheoreticalF(int n, int m) => (double)n * n * m;

        public static double[,] GenerateMatrix(int rows, int cols, Random rnd)
        {
            var mtx = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    mtx[i, j] = rnd.NextDouble() * 10;
            return mtx;
        }
    }
}
