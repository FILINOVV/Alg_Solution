namespace LabApp.Algorithms
{
    public static class PowerAlgorithms
    {
        public static int SimpleIterativeSteps(double x, int n)
        {
            double f = 1;
            int steps = 0;
            for (int k = 0; k < n; k++)
            {
                f *= x;
                steps++;
            }
            return steps;
        }

        public static int RecursiveSteps(double x, int n)
        {
            if (n == 0) return 0;
            return 1 + RecursiveSteps(x, n - 1);
        }

        public static int FastBinarySteps(double x, int n)
        {
            double c = x;
            int k = n;
            int steps = 0;
            double f = (k % 2 == 1) ? c : 1;

            while (k != 0)
            {
                k /= 2;
                c = c * c; steps++;
                if (k % 2 == 1) { f *= c; steps++; }
            }
            return steps;
        }
    }
}
