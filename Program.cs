using System;
using DokanNet;

namespace FlexFs
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            try
            {
                // Check arguments
                if (args.Length == 1 && args[0].Length == 1)
                {
                    // Get the drive letter
                    string driveLetter = args[0];
                    // Construct and mount the file system
                    FlexFileSystem f = new FlexFileSystem();
                    f.Mount(driveLetter + @":\", DokanOptions.DebugMode | DokanOptions.StderrOutput);
                }
                else
                    Console.WriteLine(@"Usage: FlexFS <drive_letter>");
            }
            catch (DokanException ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}