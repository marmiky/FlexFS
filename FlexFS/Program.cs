using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

using FlexFsLib;
using NDesk.Options;

namespace FlexFs
{
    internal class Program
    {
        static string programName;

        static void ShowHelp(OptionSet p)
        {
            Console.WriteLine("Usage: " + programName + " [OPTIONS] drive_letter [conf_file]");
            Console.WriteLine("Mount a flexible configurable file system");
            Console.WriteLine();
            Console.WriteLine("Options:");
            p.WriteOptionDescriptions(Console.Out);
        }

        private static void Main(string[] args)
        {
            bool debug = false;
            bool show_help = false;
            string confFileName = "fs.conf";
            string driveLetter = "o";
            // Extra parameters
            List<string> extra;
            try
            {
                // Get assembly name
                programName = Assembly.GetExecutingAssembly().GetName().Name;
                // Set parameters
                var p = new OptionSet() {
                    { "c|conf=", "set configuration file",
                       v => confFileName = Path.GetFullPath(v) },
                    { "d|debug", "enable debug mode",
                       v => debug = v != null },
                    { "h|help",  "show this message and exit",
                       v => show_help = v != null },
                };
                // Get extra arguments
                extra = p.Parse(args);
                // Manage help request
                if (show_help)
                {
                    ShowHelp(p);
                    return;
                }

                // Check extra arguments
                if (extra.Count >= 1 && extra[0].Length == 1)
                    // Get the drive letter
                    driveLetter = extra[0];

                // Is debug mode?
                if(debug)
                    // Start from console
                    Launcher.MountFileSystem(true, driveLetter, confFileName);
                else // Start as background process
                    Process.Start("FlexFSbg.exe", string.Format("{0} {1}", driveLetter, confFileName));
            }
            catch (OptionException e)
            {
                Console.WriteLine("Error: " + e.ToString());
                Console.WriteLine("Try '" + programName + " --help' for more information.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.ToString());
            }
        }

    }
}