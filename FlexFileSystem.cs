using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;

using DokanNet;
using FileAccess = DokanNet.FileAccess;
using static DokanNet.FormatProviders;
using DokanNet.Logging;

namespace FlexFs
{
    internal class FlexFileSystem : IDokanOperations
    {
        // Dictionary containing logical paths associations
        private readonly Dictionary<string, string[]> dirConf;

        private const FileAccess DataAccess = FileAccess.ReadData | FileAccess.WriteData | FileAccess.AppendData |
                                      FileAccess.Execute |
                                      FileAccess.GenericExecute | FileAccess.GenericWrite |
                                      FileAccess.GenericRead;

        private const FileAccess DataWriteAccess = FileAccess.WriteData | FileAccess.AppendData |
                                                   FileAccess.Delete |
                                                   FileAccess.GenericWrite;

        private ConsoleLogger logger = new ConsoleLogger("[Flex] ");

        private NtStatus Trace(string method, string fileName, DokanFileInfo info, NtStatus result,
            params object[] parameters)
        {
#if TRACE
            var extraParameters = parameters != null && parameters.Length > 0
                ? ", " + string.Join(", ", parameters.Select(x => string.Format(DefaultFormatProvider, "{0}", x)))
                : string.Empty;

            logger.Debug(DokanFormat($"{method}('{fileName}', {info}{extraParameters}) -> {result}"));
#endif

            return result;
        }

        private NtStatus Trace(string method, string fileName, DokanFileInfo info,
            FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes,
            NtStatus result)
        {
#if TRACE
            logger.Debug(
                DokanFormat(
                    $"{method}('{fileName}', {info}, [{access}], [{share}], [{mode}], [{options}], [{attributes}]) -> {result}"));
#endif

            return result;
        }

        // Constructor
        public FlexFileSystem(string confFileName)
        {
            dirConf = LoadConf(confFileName);
        }

        // Load configuration and adapt data
        private Dictionary<string, string[]> LoadConf(string confFileName)
        {
            string[] confData = File.ReadAllLines(confFileName);
            Dictionary<string, string[]> retValue = new Dictionary<string, string[]>();
            // Scan file lines
            foreach (string confLine in confData)
            {
                // Get data
                string[] cData = confLine.Split('>');
                string key = cData[0].Trim();
                string[] vals = cData[1].Split('|');
                // Correct wrong keys
                if (key != @"\")
                {
                    // If key dont end with slash add it
                    if (key.EndsWith(@"\"))
                        key = key.Substring(0, key.Length - 1);
                    // If key dont start with slash add it
                    if (!key.StartsWith(@"\"))
                        key = @"\" + key;
                }
                // Correct wrong values
                for (int i = 0; i < vals.Length; i++)
                {
                    vals[i] = vals[i].Trim();
                    // If value dont end with slash add it
                    if (!vals[i].EndsWith(@"\"))
                        vals[i] += @"\";
                }
                // Add the values to final dictionary
                retValue.Add(key, vals);
            }

            return retValue;
        }

        // Get the real path
        private string GetPath(string path)
        {
            string retPath = path;

            // Scan logical paths
            foreach (KeyValuePair<string, string[]> lDir in dirConf)
            {
                // Add a slash to directory for check sub dirs
                string slashedDir = lDir.Key;
                if (!slashedDir.EndsWith(@"\"))
                    slashedDir += @"\";

                // Check dir and subdirs
                if ((retPath == lDir.Key) || retPath.StartsWith(slashedDir))
                {
                    string relPath = (retPath.Length > slashedDir.Length ? retPath.Substring(slashedDir.Length) : "");
                    // Scan all directories
                    if (relPath != "")
                        for (int i = 0; i < lDir.Value.Length; i++)
                        {
                            // Generate the real path
                            retPath = lDir.Value[i] + relPath;

                            // If exist return it
                            if (File.Exists(retPath) || Directory.Exists(retPath))
                                return retPath;
                        }
                    // Else return the first path
                    retPath = lDir.Value[0] + relPath;
                    return retPath;
                }
            }
            // If this occours something is wrong in your configuration
            throw new Exception("Path not found! " + path);
        }

        // Get the real paths
        private string[] GetPaths(string path)
        {
            // Scan logical paths
            foreach (KeyValuePair<string, string[]> lDir in dirConf)
            {
                // Add a slash to directory for check sub dirs
                string slashedDir = lDir.Key;
                if (!slashedDir.EndsWith(@"\"))
                    slashedDir += @"\";

                // Check dir and subdirs
                if ((path == lDir.Key) || path.StartsWith(slashedDir))
                {
                    // Get the relative path
                    string relPath = (path.Length > slashedDir.Length ? path.Substring(slashedDir.Length) : "");
                    // Get all logical dirs (copy to a new array)
                    string[] dirs = new string[lDir.Value.Length];
                    lDir.Value.CopyTo(dirs, 0);
                    // If the relative path is not empty 
                    if (relPath != "")
                        // Scan all directories
                        for (int i = 0; i < dirs.Length; i++)
                            // Gen real paths
                            dirs[i] = dirs[i] + relPath;
                    // Return the directories
                    return dirs;
                }
            }
            // If this occours something is wrong in your configuration
            throw new Exception("Path not found! " + path);
        }

        // Get folder name from a path
        private string GetFolderName(string path)
        {
            string retValue = path.Trim();
            // Root path
            if (path == @"\")
                return "root";
            // Scan logical paths
            foreach (KeyValuePair<string, string[]> lDir in dirConf)
            {
                // Add a slash to directory for check sub dirs
                string slashedDir = lDir.Key;
                if (!slashedDir.EndsWith(@"\"))
                    slashedDir += @"\";
                // Check dir and subdirs
                if ((path == lDir.Key) || path.StartsWith(slashedDir))
                {
                    // Get the name on right side
                    int lastSlash = lDir.Key.LastIndexOf(@"\");
                    retValue = lDir.Key.Substring(lastSlash + 1);
                    return retValue;
                }
            }
            // When no found get the real folder name
            DirectoryInfo dInfo = new DirectoryInfo(path);
            return dInfo.Name;
        }

        // Function to count a char inside a string
        private int countCharInsideString(string stringToCheck, char charToCount)
        {
            return stringToCheck.Split(charToCount).Length - 1;
        }

        public IList<FileInformation> FindFilesHelper(string fileName, string searchPattern)
        {
            IList<FileInformation> files = new List<FileInformation>();
            // Hashset to check if a file is just added
            HashSet<string> filesAdded = new HashSet<string>();

            // First add logical files
            int fileSlashCount = 1;
            if (fileName != @"\")
                fileSlashCount = countCharInsideString(fileName, '\\') + 1;

            // Scan Logical entries
            foreach (string logicalPath in dirConf.Keys)
            {
                // Ignore root logical path
                if (logicalPath == @"\")
                    continue;

                // Check backslash and root folder
                int slashCount = countCharInsideString(logicalPath, '\\');
                if (slashCount != fileSlashCount || !logicalPath.StartsWith(fileName))
                    continue;

                // Translate the path
                string dirName = GetFolderName(logicalPath);

                // If file just added before ignore and go on
                if (filesAdded.Contains(dirName))
                    continue;

                // If not match the searchPattern  ignore and go on
                if (!DokanHelper.DokanIsNameInExpression(searchPattern, dirName, true))
                    continue;

                // Add a directory
                var finfo = new FileInformation
                {
                    FileName = dirName,
                    Attributes = FileAttributes.Directory,
                    LastAccessTime = DateTime.Now,
                    LastWriteTime = null,
                    CreationTime = null
                };
                files.Add(finfo);
                filesAdded.Add(finfo.FileName);
            }

            // Get real dirs from logical paths
            string[] realDirs = GetPaths(fileName);
            // Scan real dirs
            foreach (string realDir in realDirs)
            {
                // If the directory not exist skip it
                if (!Directory.Exists(realDir))
                    continue;
                // Get file list from real dir
                IEnumerable<FileInformation> fEnum = new DirectoryInfo(realDir)
                .EnumerateFileSystemInfos()
                .Where(finfo => DokanHelper.DokanIsNameInExpression(searchPattern, finfo.Name, true))
                .Select(finfo => new FileInformation
                {
                    Attributes = finfo.Attributes,
                    CreationTime = finfo.CreationTime,
                    LastAccessTime = finfo.LastAccessTime,
                    LastWriteTime = finfo.LastWriteTime,
                    Length = (finfo as FileInfo)?.Length ?? 0,
                    FileName = finfo.Name
                });
                // Merge to existing list and discard doubles
                files = files.Union(fEnum).GroupBy(o => o.FileName).Select(g => g.First()).ToList();
            }
            // Return the complete file list
            return files;
        }

        #region DokanOperations member

        public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode,
             FileOptions options, FileAttributes attributes, DokanFileInfo info)
        {
            var result = NtStatus.Success;
            var filePath = GetPath(fileName);

            if (info.IsDirectory)
            {
                try
                {
                    switch (mode)
                    {
                        case FileMode.Open:
                            if (!Directory.Exists(filePath))
                            {
                                try
                                {
                                    if (!File.GetAttributes(filePath).HasFlag(FileAttributes.Directory))
                                        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                            attributes, NtStatus.NotADirectory);
                                }
                                catch (Exception)
                                {
                                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                        attributes, DokanResult.FileNotFound);
                                }
                                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                    attributes, DokanResult.PathNotFound);
                            }

                            new DirectoryInfo(filePath).EnumerateFileSystemInfos().Any();
                            // you can't list the directory
                            break;

                        case FileMode.CreateNew:
                            if (Directory.Exists(filePath))
                                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                    attributes, DokanResult.FileExists);

                            try
                            {
                                File.GetAttributes(filePath).HasFlag(FileAttributes.Directory);
                                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                    attributes, DokanResult.AlreadyExists);
                            }
                            catch (IOException)
                            {
                            }

                            Directory.CreateDirectory(GetPath(fileName));
                            break;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                        DokanResult.AccessDenied);
                }
            }
            else
            {
                var pathExists = true;
                var pathIsDirectory = false;

                var readWriteAttributes = (access & DataAccess) == 0;
                var readAccess = (access & DataWriteAccess) == 0;

                try
                {
                    pathExists = (Directory.Exists(filePath) || File.Exists(filePath));
                    pathIsDirectory = File.GetAttributes(filePath).HasFlag(FileAttributes.Directory);
                }
                catch (IOException)
                {
                }

                switch (mode)
                {
                    case FileMode.Open:

                        if (pathExists)
                        {
                            if (readWriteAttributes || pathIsDirectory)
                            // check if driver only wants to read attributes, security info, or open directory
                            {
                                if (pathIsDirectory && (access & FileAccess.Delete) == FileAccess.Delete
                                    && (access & FileAccess.Synchronize) != FileAccess.Synchronize)
                                    //It is a DeleteFile request on a directory
                                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                        attributes, DokanResult.AccessDenied);

                                info.IsDirectory = pathIsDirectory;
                                info.Context = new object();
                                // must set it to someting if you return DokanError.Success

                                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                    attributes, DokanResult.Success);
                            }
                        }
                        else
                        {
                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                                DokanResult.FileNotFound);
                        }
                        break;

                    case FileMode.CreateNew:
                        if (pathExists)
                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                                DokanResult.FileExists);
                        break;

                    case FileMode.Truncate:
                        if (!pathExists)
                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                                DokanResult.FileNotFound);
                        break;
                }

                try
                {
                    info.Context = new FileStream(filePath, mode,
                        readAccess ? System.IO.FileAccess.Read : System.IO.FileAccess.ReadWrite, share, 4096, options);

                    if (pathExists && (mode == FileMode.OpenOrCreate
                        || mode == FileMode.Create))
                        result = DokanResult.AlreadyExists;

                    if (mode == FileMode.CreateNew || mode == FileMode.Create) //Files are always created as Archive
                        attributes |= FileAttributes.Archive;

                    if(mode != FileMode.Open) // Why set attributes on file open?
                        File.SetAttributes(filePath, attributes);
                }
                catch (UnauthorizedAccessException) // don't have access rights
                {
                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                        DokanResult.AccessDenied);
                }
                catch (DirectoryNotFoundException)
                {
                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                        DokanResult.PathNotFound);
                }
                catch (Exception ex)
                {
                    var hr = (uint)Marshal.GetHRForException(ex);
                    switch (hr)
                    {
                        case 0x80070020: //Sharing violation
                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                                DokanResult.SharingViolation);
                        default:
                            throw;
                    }
                }
            }
            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                result);
        }

        public void Cleanup(string fileName, DokanFileInfo info)
        {
#if TRACE
            if (info.Context != null)
                Console.WriteLine(DokanFormat($"{nameof(Cleanup)}('{fileName}', {info} - entering"));
#endif

            (info.Context as FileStream)?.Dispose();
            info.Context = null;

            if (info.DeleteOnClose)
            {
                if (info.IsDirectory)
                {
                    Directory.Delete(GetPath(fileName));
                }
                else
                {
                    File.Delete(GetPath(fileName));
                }
            }
            Trace(nameof(Cleanup), fileName, info, DokanResult.Success);
        }

        public void CloseFile(string fileName, DokanFileInfo info)
        {
#if TRACE
            if (info.Context != null)
                Console.WriteLine(DokanFormat($"{nameof(CloseFile)}('{fileName}', {info} - entering"));
#endif

            (info.Context as FileStream)?.Dispose();
            info.Context = null;
            Trace(nameof(CloseFile), fileName, info, DokanResult.Success);
            // could recreate cleanup code here but this is not called sometimes
        }

        public NtStatus FlushFileBuffers(string fileName, DokanFileInfo info)
        {
            try
            {
                ((FileStream)(info.Context)).Flush();
                return Trace(nameof(FlushFileBuffers), fileName, info, DokanResult.Success);
            }
            catch (IOException)
            {
                return Trace(nameof(FlushFileBuffers), fileName, info, DokanResult.DiskFull);
            }
        }

        public NtStatus FindFiles(string fileName, out IList<FileInformation> files, DokanFileInfo info)
        {
            // This function is not called because FindFilesWithPattern is implemented
            // Return DokanResult.NotImplemented in FindFilesWithPattern to make FindFiles called
            files = FindFilesHelper(fileName, "*");

            return Trace(nameof(FindFiles), fileName, info, DokanResult.Success);
        }

        public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files,
            DokanFileInfo info)
        {
            files = FindFilesHelper(fileName, searchPattern);

            return Trace(nameof(FindFilesWithPattern), fileName, info, DokanResult.Success);
        }

        public NtStatus GetFileInformation(
            string fileName,
            out FileInformation fileInfo,
            DokanFileInfo info)
        {

            fileInfo = new FileInformation { FileName = fileName };

            // Get the real path
            var filePath = GetPath(fileName);

            // Get file infos
            FileSystemInfo finfo = new FileInfo(filePath);
            if (!finfo.Exists) // Not a file? Try to get directory infos
                finfo = new DirectoryInfo(filePath);
            // Prepare out data
            fileInfo = new FileInformation
            {
                FileName = fileName,
                Attributes = finfo.Attributes,
                CreationTime = finfo.CreationTime,
                LastAccessTime = finfo.LastAccessTime,
                LastWriteTime = finfo.LastWriteTime,
                Length = (finfo as FileInfo)?.Length ?? 0,
            };

            return Trace(nameof(GetFileInformation), fileName, info, DokanResult.Success);
        }

        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
        {
            try
            {
                File.SetAttributes(GetPath(fileName), attributes);
                return Trace(nameof(SetFileAttributes), fileName, info, DokanResult.Success, attributes.ToString());
            }
            catch (UnauthorizedAccessException)
            {
                return Trace(nameof(SetFileAttributes), fileName, info, DokanResult.AccessDenied, attributes.ToString());
            }
            catch (FileNotFoundException)
            {
                return Trace(nameof(SetFileAttributes), fileName, info, DokanResult.FileNotFound, attributes.ToString());
            }
            catch (DirectoryNotFoundException)
            {
                return Trace(nameof(SetFileAttributes), fileName, info, DokanResult.PathNotFound, attributes.ToString());
            }
        }

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime,
            DateTime? lastWriteTime, DokanFileInfo info)
        {
            try
            {
                var filePath = GetPath(fileName);
                if (creationTime.HasValue)
                    File.SetCreationTime(filePath, creationTime.Value);

                if (lastAccessTime.HasValue)
                    File.SetLastAccessTime(filePath, lastAccessTime.Value);

                if (lastWriteTime.HasValue)
                    File.SetLastWriteTime(filePath, lastWriteTime.Value);

                return Trace(nameof(SetFileTime), fileName, info, DokanResult.Success, creationTime, lastAccessTime,
                    lastWriteTime);
            }
            catch (UnauthorizedAccessException)
            {
                return Trace(nameof(SetFileTime), fileName, info, DokanResult.AccessDenied, creationTime, lastAccessTime,
                    lastWriteTime);
            }
            catch (FileNotFoundException)
            {
                return Trace(nameof(SetFileTime), fileName, info, DokanResult.FileNotFound, creationTime, lastAccessTime,
                    lastWriteTime);
            }
        }

        public NtStatus DeleteFile(string fileName, DokanFileInfo info)
        {
            var filePath = GetPath(fileName);

            if (Directory.Exists(filePath))
                return Trace(nameof(DeleteFile), fileName, info, DokanResult.AccessDenied);

            if (!File.Exists(filePath))
                return Trace(nameof(DeleteFile), fileName, info, DokanResult.FileNotFound);

            if (File.GetAttributes(filePath).HasFlag(FileAttributes.Directory))
                return Trace(nameof(DeleteFile), fileName, info, DokanResult.AccessDenied);

            return Trace(nameof(DeleteFile), fileName, info, DokanResult.Success);
            // we just check here if we could delete the file - the true deletion is in Cleanup
        }

        public NtStatus DeleteDirectory(string fileName, DokanFileInfo info)
        {
            return Trace(nameof(DeleteDirectory), fileName, info,
                Directory.EnumerateFileSystemEntries(GetPath(fileName)).Any()
                    ? DokanResult.DirectoryNotEmpty
                    : DokanResult.Success);
            // if dir is not empty it can't be deleted
        }

        public NtStatus MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
        {
            var oldpath = GetPath(oldName);
            var newpath = GetPath(newName);

            (info.Context as FileStream)?.Dispose();
            info.Context = null;

            var exist = info.IsDirectory ? Directory.Exists(newpath) : File.Exists(newpath);

            try
            {

                if (!exist)
                {
                    info.Context = null;
                    if (info.IsDirectory)
                        Directory.Move(oldpath, newpath);
                    else
                        File.Move(oldpath, newpath);
                    return Trace(nameof(MoveFile), oldName, info, DokanResult.Success, newName,
                        replace.ToString(CultureInfo.InvariantCulture));
                }
                else if (replace)
                {
                    info.Context = null;

                    if (info.IsDirectory) //Cannot replace directory destination - See MOVEFILE_REPLACE_EXISTING
                        return Trace(nameof(MoveFile), oldName, info, DokanResult.AccessDenied, newName,
                            replace.ToString(CultureInfo.InvariantCulture));

                    File.Delete(newpath);
                    File.Move(oldpath, newpath);
                    return Trace(nameof(MoveFile), oldName, info, DokanResult.Success, newName,
                        replace.ToString(CultureInfo.InvariantCulture));
                }
            }
            catch (UnauthorizedAccessException)
            {
                return Trace(nameof(MoveFile), oldName, info, DokanResult.AccessDenied, newName,
                    replace.ToString(CultureInfo.InvariantCulture));
            }
            return Trace(nameof(MoveFile), oldName, info, DokanResult.FileExists, newName,
                replace.ToString(CultureInfo.InvariantCulture));
        }

        public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, DokanFileInfo info)
        {
            if (info.Context == null) // memory mapped read
            {
                string filePath = GetPath(fileName);

                using (var stream = new FileStream(filePath, FileMode.Open, System.IO.FileAccess.Read))
                {
                    stream.Position = offset;
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
            }
            else // normal read
            {
                var stream = info.Context as FileStream;
                lock (stream) //Protect from overlapped read
                {
                    stream.Position = offset;
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
            }
            return Trace(nameof(ReadFile), fileName, info, DokanResult.Success, "out " + bytesRead.ToString(),
                offset.ToString(CultureInfo.InvariantCulture));
        }

        public NtStatus SetEndOfFile(string fileName, long length, DokanFileInfo info)
        {
            try
            {
                ((FileStream)(info.Context)).SetLength(length);
                return Trace(nameof(SetEndOfFile), fileName, info, DokanResult.Success,
                    length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace(nameof(SetEndOfFile), fileName, info, DokanResult.DiskFull,
                    length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus SetAllocationSize(string fileName, long length, DokanFileInfo info)
        {
            try
            {
                ((FileStream)(info.Context)).SetLength(length);
                return Trace(nameof(SetAllocationSize), fileName, info, DokanResult.Success,
                    length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace(nameof(SetAllocationSize), fileName, info, DokanResult.DiskFull,
                    length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus LockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
#if !NETCOREAPP1_0
            try
            {
                ((FileStream)(info.Context)).Lock(offset, length);
                return Trace(nameof(LockFile), fileName, info, DokanResult.Success,
                    offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace(nameof(LockFile), fileName, info, DokanResult.AccessDenied,
                    offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
#else
            // .NET Core 1.0 do not have support for FileStream.Lock
            return NtStatus.NotImplemented;
#endif
        }

        public NtStatus UnlockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
#if !NETCOREAPP1_0
            try
            {
                ((FileStream)(info.Context)).Unlock(offset, length);
                return Trace(nameof(UnlockFile), fileName, info, DokanResult.Success,
                    offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
                return Trace(nameof(UnlockFile), fileName, info, DokanResult.AccessDenied,
                    offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
#else
            // .NET Core 1.0 do not have support for FileStream.Unlock
            return NtStatus.NotImplemented;
#endif
        }


        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetDiskFreeSpaceEx(string lpDirectoryName, out ulong lpFreeBytesAvailable, out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);

        public NtStatus GetDiskFreeSpace(
            out long freeBytesAvailable,
            out long totalBytes,
            out long totalFreeBytes,
            DokanFileInfo info)
        {
            ulong FreeBytesAvailable;
            ulong TotalNumberOfBytes;
            ulong TotalNumberOfFreeBytes;
            string driveName;
            HashSet<string> drives = new HashSet<string>();
            // Init sizes
            freeBytesAvailable = 0;
            totalBytes = 0;
            totalFreeBytes = 0;
            // Scan all real paths
            foreach (string[] paths in dirConf.Values)
            {
                foreach (string path in paths)
                {
                    // Get the slash position
                    int slashPos = 0;
                    // Is a network share?
                    if (path.StartsWith(@"\\"))
                    {
                        // Skip the 2 initial slashes
                        slashPos = path.IndexOf(@"\", 2);
                        // Skip also the third and get the fourth
                        slashPos = path.IndexOf(@"\", slashPos + 1);
                    }
                    else
                        slashPos = path.IndexOf(@"\");
                    // Get the drive name
                    driveName = path.Substring(0, slashPos + 1);
                    // Add the drive
                    if (!drives.Contains(driveName))
                        drives.Add(driveName);
                }
            }
            // Scan drives
            foreach (string drive in drives)
            {
                // Call the windows api
                bool success = GetDiskFreeSpaceEx(drive, out FreeBytesAvailable, out TotalNumberOfBytes, out TotalNumberOfFreeBytes);
                if (success)
                {
                    // Add sizes
                    freeBytesAvailable += (long)FreeBytesAvailable;
                    totalBytes += (long)TotalNumberOfBytes;
                    totalFreeBytes += (long)TotalNumberOfFreeBytes;
                }
            }
            return DokanResult.Success;
        }

        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, DokanFileInfo info)
        {
            if (info.Context == null)
            {
                string filePath = GetPath(fileName);

                using (var stream = new FileStream(filePath, FileMode.Open, System.IO.FileAccess.Write))
                {
                    stream.Position = offset;
                    stream.Write(buffer, 0, buffer.Length);
                    bytesWritten = buffer.Length;
                }
            }
            else
            {
                var stream = info.Context as FileStream;
                lock (stream) //Protect from overlapped write
                {
                    stream.Position = offset;
                    stream.Write(buffer, 0, buffer.Length);
                }
                bytesWritten = buffer.Length;
            }
            return Trace(nameof(WriteFile), fileName, info, DokanResult.Success, "out " + bytesWritten.ToString(),
                offset.ToString(CultureInfo.InvariantCulture));
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features,
            out string fileSystemName, DokanFileInfo info)
        {
            volumeLabel = "FlexFS";
            fileSystemName = "NTFS";

            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch |
                       FileSystemFeatures.PersistentAcls | FileSystemFeatures.SupportsRemoteStorage |
                       FileSystemFeatures.UnicodeOnDisk;

            return Trace(nameof(GetVolumeInformation), null, info, DokanResult.Success, "out " + volumeLabel,
                "out " + features.ToString(), "out " + fileSystemName);
        }

        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections,
            DokanFileInfo info)
        {
#if !NETCOREAPP1_0
            try
            {
                security = info.IsDirectory
                    ? (FileSystemSecurity)Directory.GetAccessControl(GetPath(fileName))
                    : File.GetAccessControl(GetPath(fileName));
                return Trace(nameof(GetFileSecurity), fileName, info, DokanResult.Success, sections.ToString());
            }
            catch (UnauthorizedAccessException)
            {
                security = null;
                return Trace(nameof(GetFileSecurity), fileName, info, DokanResult.AccessDenied, sections.ToString());
            }
#else
            // .NET Core 1.0 do not have support for Directory.GetAccessControl
            security = null;
            return NtStatus.NotImplemented;
#endif
        }

        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections,
                    DokanFileInfo info)
        {
#if !NETCOREAPP1_0
            try
            {
                if (info.IsDirectory)
                {
                    Directory.SetAccessControl(GetPath(fileName), (DirectorySecurity)security);
                }
                else
                {
                    File.SetAccessControl(GetPath(fileName), (FileSecurity)security);
                }
                return Trace(nameof(SetFileSecurity), fileName, info, DokanResult.Success, sections.ToString());
            }
            catch (UnauthorizedAccessException)
            {
                return Trace(nameof(SetFileSecurity), fileName, info, DokanResult.AccessDenied, sections.ToString());
            }
#else
            // .NET Core 1.0 do not have support for Directory.SetAccessControl
            return NtStatus.NotImplemented;
#endif
        }

        public NtStatus Mounted(DokanFileInfo info)
        {
            return Trace(nameof(Mounted), null, info, DokanResult.Success);
        }

        public NtStatus Unmounted(DokanFileInfo info)
        {
            return Trace(nameof(Unmounted), null, info, DokanResult.Success);
        }

        public NtStatus EnumerateNamedStreams(string fileName, IntPtr enumContext, out string streamName,
            out long streamSize, DokanFileInfo info)
        {
            streamName = string.Empty;
            streamSize = 0;
            return DokanResult.NotImplemented;
        }

        public NtStatus FindStreams(string fileName, IntPtr enumContext, out string streamName, out long streamSize, DokanFileInfo info)
        {
            streamName = string.Empty;
            streamSize = 0;
            return Trace(nameof(FindStreams), fileName, info, DokanResult.NotImplemented, enumContext.ToString(),
                "out " + streamName, "out " + streamSize.ToString());
        }

        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, DokanFileInfo info)
        {
            streams = new FileInformation[0];
            return Trace(nameof(FindStreams), fileName, info, DokanResult.NotImplemented);
        }

        #endregion DokanOperations member
    }
}