﻿using FileFlows.Plugin;
using FileFlows.Server.Controllers;
using FileFlows.Server.Helpers;
using FileFlows.Shared.Models;
using System.Text.RegularExpressions;

namespace FileFlows.Server.Workers
{
    public class WatchedLibrary:IDisposable
    {
        private FileSystemWatcher Watcher;
        public Library Library { get;private set; } 

        public List<LibraryFile> LibraryFiles { get; private set; }

        private Regex? Filter;

        private bool ScanComplete = false;
        private bool UseScanner = false;

        public WatchedLibrary(Library library)
        {
            this.Library = library;
            this.UseScanner = library.Scan;
            if(string.IsNullOrEmpty(library.Filter) == false)
            {
                try
                {
                    Filter = new Regex(library.Filter, RegexOptions.IgnoreCase);
                }
                catch (Exception) { }
            }

            LibraryFiles = DbHelper.Select<LibraryFile>("lower(Name) like lower(@1)", library.Path + "%").Result.ToList();
            if(UseScanner == false)
                SetupWatcher();
        }
        public void Dispose()
        {
            DisposeWatcher();
        }

        void SetupWatcher()
        {
            DisposeWatcher();

            Watcher = new FileSystemWatcher(Library.Path);
            Watcher.NotifyFilter =
                             //NotifyFilters.Attributes |
                             NotifyFilters.CreationTime |
                             NotifyFilters.DirectoryName |
                             NotifyFilters.FileName |
                             // NotifyFilters.LastAccess |
                             NotifyFilters.LastWrite |
                             //| NotifyFilters.Security
                             NotifyFilters.Size;
            Watcher.IncludeSubdirectories = true;
            Watcher.Changed += Watcher_Changed;
            Watcher.Created += Watcher_Changed;
            //Watcher.Deleted += Watcher_Changed;
            Watcher.Renamed += Watcher_Changed;
            Watcher.EnableRaisingEvents = true;

        }

        void DisposeWatcher()
        {
            if (Watcher != null)
            {
                Watcher.Changed -= Watcher_Changed;
                Watcher.Created -= Watcher_Changed;
                Watcher.Renamed -= Watcher_Changed;
                Watcher.EnableRaisingEvents = false;
                Watcher.Dispose();
                Watcher = null;
            }
        }

        private bool IsMatch(string input)
        {
            if (Filter == null)
                return true;
            return Filter.IsMatch(input);
        }

        private void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (Library.Folders == false && Directory.Exists(e.FullPath))
            {
                foreach(var file in Directory.GetFiles(e.FullPath, "*.*", SearchOption.AllDirectories))
                {
                    FileChangeEvent(file);
                }
            }
            else
            {
                FileChangeEvent(e.FullPath);
            }
        }

        private void FileChangeEvent(string fullPath)
        { 
            if (IsMatch(fullPath) == false)
            {
                if (fullPath.Contains("_UNPACK_"))
                    return; // dont log this, too many
                //Logger.Instance.DLog("WatchedLibrary: File does not match library pattern: " + fullPath);
                return;
            }

            // new file we can process
            if (KnownFile(fullPath))
            {
                Logger.Instance.ILog("WatchedLibrary: Known file: " + fullPath);
                return;
            }

            Logger.Instance.ILog("WatchedLibrary: Changed event detected on file: " + fullPath);

            if (IsFileLocked(fullPath) == false)
            {

                Logger.Instance.ILog("WatchedLibrary: Detected new file: " + fullPath);
                _ = AddLibraryFile(fullPath);
            }
            else
            {
                Logger.Instance.ILog("WatchedLibrary: New file detected, but currently locked: " + fullPath);
            }
        }

        internal void UpdateLibrary(Library library)
        {
            this.Library = library;

            if (UseScanner && library.Scan == false)
            {
                Logger.Instance.ILog($"WatchedLibrary: Library '{library.Name}' switched to watched mode, starting watcher");
                UseScanner = false;
                SetupWatcher();
            }
            else if(UseScanner == false && library.Scan == true)
            {
                Logger.Instance.ILog($"WatchedLibrary: Library '{library.Name}' switched to scan mode, disposing watcher");
                UseScanner = true;
                DisposeWatcher();
            }
            else if(UseScanner == false && Watcher != null && Watcher.Path != library.Path)
            {
                // library path changed, need to change watcher
                Logger.Instance.ILog($"WatchedLibrary: Library '{library.Name}' path changed, updating watched path");
                SetupWatcher(); 
            }

            if (library.LastScanned < new DateTime(2020, 1, 1))
            {
                ScanComplete = false; // this could happen if they click "Rescan" on the library page, this will force a full new scan
                Logger.Instance?.ILog($"WatchedLibrary: Library '{library.Name}' marked for full scan");
            }
        }


        private bool KnownFile(string file)
        {
            file = file.ToLower();
            return LibraryFiles.Any(x => x.Name.ToLower() == file);
        }

        private bool IsFileLocked(string file)
        {
            const int ERROR_SHARING_VIOLATION = 32;
            const int ERROR_LOCK_VIOLATION = 33;

            if (File.Exists(file))
            {
                FileStream stream = null;
                try
                {
                    stream = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                }
                catch (Exception ex2)
                {
                    //_log.WriteLog(ex2, "Error in checking whether file is locked " + file);
                    int errorCode = System.Runtime.InteropServices.Marshal.GetHRForException(ex2) & ((1 << 16) - 1);
                    if ((ex2 is IOException) && (errorCode == ERROR_SHARING_VIOLATION || errorCode == ERROR_LOCK_VIOLATION))
                    {
                        return true;
                    }
                }
                finally
                {
                    if (stream != null)
                        stream.Close();
                }
            }
            return false;
        }

        public bool Scan(bool fullScan = false)
        {
            if (Library.ScanInterval < 10)
                Library.ScanInterval = 60;

            if (Library.Enabled == false)
                return false;
            if (TimeHelper.InSchedule(Library.Schedule) == false)
                return false;

            if (Library.LastScanned > DateTime.UtcNow.AddSeconds(-Library.ScanInterval))
                return false;

            if (UseScanner == false && ScanComplete && fullScan == false)
            {
                //Logger.Instance?.ILog($"Library '{Library.Name}' has full scan, using FileWatcherEvents now to watch for new files");
                return false; // we can use the filesystem watchers for any more files
            }

            if (string.IsNullOrEmpty(Library.Path) || Directory.Exists(Library.Path) == false)
            {
                Logger.Instance?.WLog($"WatchedLibrary: Library '{Library.Name}' path not found: {Library.Path}");
                return false;
            }

            int count = LibraryFiles.Count;
            if (Library.Folders)
                ScanForDirs(Library);
            else
                ScanFoFiles(Library);

            new LibraryController().UpdateLastScanned(Library.Uid).Wait();

            ScanComplete = Library.Folders == false; // only count a full scan against files
            return count < LibraryFiles.Count;
        }


        private void ScanForDirs(Library library)
        {
            var dirs = new DirectoryInfo(library.Path).GetDirectories();
            List<string> known = new();

            foreach (var libFile in LibraryFiles)
            {
                if (libFile.IsDirectory == false)
                    continue;
                if (string.IsNullOrEmpty(libFile.Name) == false && known.Contains(libFile.Name.ToLower()) == false)
                    known.Add(libFile.Name.ToLower());
            }
            var tasks = new List<Task<LibraryFile>>();
            foreach (var dir in dirs)
            {
                if (IsMatch(dir.FullName) == false)
                    continue;

                if (known.Contains(dir.FullName.ToLower()))
                    continue; // already known

                tasks.Add(GetLibraryFile(dir));
            }
            Task.WaitAll(tasks.ToArray());

            new LibraryFileController().AddMany(tasks.Where(x => x.Result != null).Select(x => x.Result).ToArray()).Wait();
        }
        private void ScanFoFiles(Library library)
        {
            var files = GetFiles(new DirectoryInfo(library.Path));
            List<string> known = new();

            foreach (var libFile in LibraryFiles)
            {
                if (libFile.IsDirectory == true)
                    continue;
                if (string.IsNullOrEmpty(libFile.Name) == false && known.Contains(libFile.Name.ToLower()) == false)
                    known.Add(libFile.Name.ToLower());
                // need to also exclude the final output of a library file
                if (string.IsNullOrEmpty(libFile.OutputPath) == false && known.Contains(libFile.OutputPath.ToLower()) == false)
                    known.Add(libFile.OutputPath.ToLower());
            }
            var tasks = new List<Task<LibraryFile>>();
            foreach (var file in files)
            {
                if (IsMatch(file.FullName) == false || file.FullName.EndsWith("_"))
                    continue;

                if (known.Contains(file.FullName.ToLower()))
                    continue; // already known

                tasks.Add(GetLibraryFile(file));
            }
            Task.WaitAll(tasks.ToArray());

            new LibraryFileController().AddMany(tasks.Where(x => x.Result != null).Select(x => x.Result).ToArray()).Wait();
        }

        private async Task AddLibraryFile(string filename)
        {
            //var flow = GetFlow();
            //if (flow == null) 
            //{
            //    ScanComplete = false; // was bad cant process this file cos no flow
            //    return;
            //}

            var result = await GetLibraryFile(new FileInfo(filename));
            if(result != null)
                await new LibraryFileController().AddMany(new[] { result });
        }

        private Flow GetFlow()
        {
            var flow = new FlowController().Get(Library.Flow.Uid).Result;
            if (flow == null)
            {
                Logger.Instance?.WLog($"WatchedLibrary: Library '{Library.Name}' flow not found");
                return null;
            }
            return flow;
        }

        private async Task<LibraryFile> GetLibraryFile(FileSystemInfo info)
        {
            if (info is FileInfo fileInfo)
            {
                if (await CanAccess(fileInfo, Library.FileSizeDetectionInterval) == false)
                    return null; // this can happen if the file is currently being written to, next scan will retest it
            }

            var lf = new LibraryFile
            {
                Name = info.FullName,
                RelativePath = info.FullName.Substring(Library.Path.Length + 1),
                Status = FileStatus.Unprocessed,
                IsDirectory = info is DirectoryInfo,
                Library = new ObjectReference
                {
                    Name = Library.Name,
                    Uid = Library.Uid,
                    Type = Library.GetType()?.FullName ?? string.Empty
                },
                //Flow = new ObjectReference
                //{
                //    Name = flow.Name,
                //    Uid = flow.Uid,
                //    Type = flow.GetType()?.FullName ?? string.Empty
                //},
                Order = -1
            };

            LibraryFiles.Add(lf);
            return lf;
        }


        private async Task<bool> CanAccess(FileInfo file, int fileSizeDetectionInterval)
        {
            try
            {
                if (file.LastAccessTimeUtc < DateTime.UtcNow.AddSeconds(-10))
                {
                    // check if the file size changes
                    long fs = file.Length;
                    if (fileSizeDetectionInterval > 0)
                        await Task.Delay(Math.Min(300, fileSizeDetectionInterval) * 1000);

                    if (fs != file.Length)
                    {
                        Logger.Instance.ILog("WatchedLibrary: File size has changed, skipping for now: " + file.FullName);
                        return false; // file size has changed, could still be being written too
                    }
                }
                using (var fs = File.Open(file.FullName, FileMode.Open, FileAccess.ReadWrite))
                {
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public List<FileInfo> GetFiles(DirectoryInfo dir)
        {
            var files = new List<FileInfo>();
            try
            {
                foreach (var subdir in dir.GetDirectories())
                {
                    files.AddRange(GetFiles(subdir));
                }
                files.AddRange(dir.GetFiles());
            }
            catch (Exception) { }
            return files;
        }
    }
}