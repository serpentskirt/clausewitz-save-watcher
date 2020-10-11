using System;
using System.Threading;
using System.Threading.Tasks;
using clausewitz_save_watcher;

namespace clausewitz_save_watcher_example
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
                throw new ArgumentException("Source and target paths must be provided as arguments.");

            SaveWatcher sw = new SaveWatcher(args[0], args[1]);

            Console.WriteLine("Backing up save games until Enter is pressed...");
            Console.WriteLine("from: {0}", args[0]);
            Console.WriteLine("to: {0}", args[1]);

            Task.Run(() => sw.Start());

            Console.ReadLine();
            sw.Stop();
        }
    }
}
