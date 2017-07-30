using System;

using FlexFsLib;

namespace FlexFSbg
{
    static class Program
    {
        /// <summary>
        /// Flex FS Background application (running on normal mode)
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Get the drive letter
            string driveLetter = args[0];
            // Set the conf file
            string confFileName = args[1];
            // Start the file system launcher
            Launcher.MountFileSystem(false, driveLetter, confFileName);
        }
    }
}
