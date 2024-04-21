namespace LinuxWebJobs
{
    internal class Program
    {
        static void Main(string[] args)
        {
            while (true)
            {
                Thread.Sleep(1000);

                var now = DateTime.Now;
                Console.WriteLine($"{now}");
            }
        }
    }
}
