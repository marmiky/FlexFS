using System;
using System.IO;

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
                if(args.Length >= 1 && args.Length <= 2 && args[0].Length == 1)
                {
                    // Get the drive letter
                    string driveLetter = args[0];
                    // Set the conf file
                    string confFileName = "fs.conf";
                    if (args.Length > 1)
                        confFileName = Path.GetFullPath(args[1]);
                    // Construct and mount the file system
                    FlexFileSystem f = new FlexFileSystem(confFileName);
                    f.Mount(driveLetter + @":\", DokanOptions.DebugMode | DokanOptions.StderrOutput);
                }
                else
                    Console.WriteLine(@"Usage: FlexFS <drive_letter> [<conf_file>]");
            }
            catch (DokanException ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }

    }
}