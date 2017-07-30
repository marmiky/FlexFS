using DokanNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FlexFsLib
{
    public static class Launcher
    { // Class used sor start the filesystem (don't need Dokan reference)
        public static void MountFileSystem(bool DebugMode, string DriveLetter, string ConfFileName)
        {
            // Construct and mount the file system
            FlexFileSystem f = new FlexFileSystem(ConfFileName);
            if (DebugMode)
                f.Mount(DriveLetter + @":\", DokanOptions.DebugMode | DokanOptions.StderrOutput);
            else
                f.Mount(DriveLetter + @":\");
        }
    }
}
