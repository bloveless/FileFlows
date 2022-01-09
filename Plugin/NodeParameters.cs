namespace FileFlows.Plugin
{
    using System.Collections.Generic;
    using System.Text;

    public class NodeParameters
    {
        /// <summary>
        /// The original filename of the file
        /// </summary>
        public string FileName { get; init; }
        /// <summary>
        /// Gets or sets the file relative to the library path
        /// </summary>
        public string RelativeFile { get; set; }

        /// <summary>
        /// The current working file as it is being processed in the flow, 
        /// this is what a node should save any changes too, and if the node needs 
        /// to change the file path this should be updated too
        /// </summary>
        public string WorkingFile { get; private set; }

        public long WorkingFileSize { get; private set; }

        public ILogger? Logger { get; set; }

        public NodeResult Result { get; set; } = NodeResult.Success;

        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> Variables { get; set; } = new Dictionary<string, object>();

        public Func<string, string>? GetToolPath { get; set; }
        public Func<string, string>? PathMapper { get; set; }

        public Action<ObjectReference> GotoFlow { get; set; }

        public bool IsDirectory { get; set; }
        public string LibraryPath { get; set; }

        public string TempPath { get; set; }

        public Action<float>? PartPercentageUpdate { get; set; }

        public ProcessHelper Process { get; set; }

        public NodeParameters(string filename, ILogger logger, bool isDirectory, string libraryPath)
        {
            this.IsDirectory = isDirectory;
            this.FileName = filename;
            this.LibraryPath = libraryPath;
            this.WorkingFile = filename;
            try
            {
                this.WorkingFileSize = IsDirectory ? GetDirectorySize(filename) : new FileInfo(filename).Length;
            }
            catch (Exception) { } // can fail in unit tests
            this.RelativeFile = string.Empty;
            this.TempPath = string.Empty;
            this.Logger = logger;
            InitFile(filename);
            this.Process = new ProcessHelper(logger);
        }


        public long GetDirectorySize(string path)
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(path);
                return dir.EnumerateFiles("*.*", SearchOption.AllDirectories).Sum(x => x.Length);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public string MapPath(string path)
        {
            if (PathMapper == null)
                return path;
            return PathMapper(path);
        }

        private void InitFile(string filename)
        {
            try
            {
                if (IsDirectory)
                {
                    var di = new DirectoryInfo(filename);
                    var diOriginal = new DirectoryInfo(FileName);
                    UpdateVariables(new Dictionary<string, object> {
                        { "folder.Name", di.Name ?? "" },
                        { "folder.FullName", di.FullName ?? "" },

                        { "folder.Date", diOriginal.CreationTime },
                        { "folder.Date.Year", diOriginal.CreationTime.Year },
                        { "folder.Date.Month", diOriginal.CreationTime.Month },
                        { "folder.Date.Day", diOriginal.CreationTime.Day},

                        { "folder.Orig.Name", diOriginal.Name ?? "" },
                        { "folder.Orig.FullName", diOriginal.FullName ?? "" },
                    });
                }
                else
                {
                    var fi = new FileInfo(filename);
                    var fiOriginal = new FileInfo(FileName);
                    UpdateVariables(new Dictionary<string, object> {
                        { "ext", fi.Extension ?? "" },
                        { "file.Name", Path.GetFileNameWithoutExtension(fi.Name ?? "") },
                        { "file.FullName", fi.FullName ?? "" },
                        { "file.Extension", fi.Extension ?? "" },
                        { "file.Size", fi.Exists ? fi.Length : 0 },
                        { "file.Create", fiOriginal.CreationTime },
                        { "file.Create.Year", fiOriginal.CreationTime.Year },
                        { "file.Create.Month", fiOriginal.CreationTime.Month },
                        { "file.Create.Day", fiOriginal.CreationTime.Day },

                        { "file.Modified", fiOriginal.LastWriteTime },
                        { "file.Modified.Year", fiOriginal.LastWriteTime.Year },
                        { "file.Modified.Month", fiOriginal.LastWriteTime.Month },
                        { "file.Modified.Day", fiOriginal.LastWriteTime.Day },

                        { "file.Orig.Extension", fiOriginal.Extension ?? "" },
                        { "file.Orig.FileName", Path.GetFileNameWithoutExtension(fiOriginal.Name ?? "") },
                        { "file.Orig.FullName", fiOriginal.FullName ?? "" },
                        { "file.Orig.Size", fiOriginal.Exists ? fiOriginal.Length : 0 },

                        { "folder.Name", fi.Directory?.Name ?? "" },
                        { "folder.FullName", fi.DirectoryName ?? "" },

                        { "folder.Orig.Name", fiOriginal.Directory?.Name ?? "" },
                        { "folder.Orig.FullName", fiOriginal.DirectoryName ?? "" },
                    });
                }
            }
            catch (Exception) { }

        }

        public void ResetWorkingFile()
        {
            SetWorkingFile(this.FileName, dontDelete: true);
        }

        public void SetWorkingFile(string filename, bool dontDelete = false)
        {
            if (this.WorkingFile == filename)
                return;
            if (this.WorkingFile != this.FileName)
            {
                this.WorkingFileSize = new FileInfo(filename).Length;
                string fileToDelete = this.WorkingFile;
                if (dontDelete == false)
                {
                    // delete the old working file
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2_000); // wait 2 seconds for the file to be released if used
                        try
                        {
                            File.Delete(fileToDelete);
                        }
                        catch (Exception ex)
                        {
                            Logger?.WLog("Failed to delete temporary file: " + ex.Message + Environment.NewLine + ex.StackTrace);
                        }
                    });
                }
            }
            this.WorkingFile = filename;
            InitFile(filename);
        }

        public T GetParameter<T>(string name)
        {
            if (Parameters.ContainsKey(name) == false)
            {
                if (typeof(T) == typeof(string))
                    return (T)(object)string.Empty;
                return default(T)!;
            }
            return (T)Parameters[name];
        }

        public void SetParameter(string name, object value)
        {
            if (Parameters.ContainsKey(name) == false)
                Parameters[name] = value;
            else
                Parameters.Add(name, value);
        }

        public bool MoveFile(string destination)
        {
            bool moved = false;
            Logger?.ILog("About to move file to: " + destination);
            destination = MapPath(destination);
            long fileSize = new FileInfo(WorkingFile).Length;
            Task task = Task.Run(() =>
            {
                try
                {
                    var fileInfo = new FileInfo(destination);
                    if (fileInfo.Exists)
                        fileInfo.Delete();
                    else
                        CreateDirectoryIfNotExists(fileInfo?.DirectoryName);

                    Logger?.ILog($"Moving file: \"{WorkingFile}\" to \"{destination}\"");                    
                    System.IO.File.Move(WorkingFile, destination, true);
                    Logger?.ILog("File moved successfully");

                    this.WorkingFile = destination;
                    try
                    {
                        // this can fail if the file is then moved really quickly by another process, radarr/sonarr etc
                        Logger?.ILog("Initing new moved file");
                        InitFile(destination);
                    }
                    catch (Exception ex) { }

                    moved = true;
                }
                catch (Exception ex)
                {
                    Logger?.ELog("Failed to move file: " + ex.Message);
                }
            });

            while (task.IsCompleted == false)
            {
                long currentSize = 0;
                var destFileInfo = new FileInfo(destination);
                if (destFileInfo.Exists)
                    currentSize = destFileInfo.Length;

                if (PartPercentageUpdate != null)
                    PartPercentageUpdate(currentSize / fileSize * 100);
                Thread.Sleep(50);
            }

            if (moved == false)
                return false;

            if (PartPercentageUpdate != null)
                PartPercentageUpdate(100);
            return true;
        }

        public void UpdateVariables(Dictionary<string, object> updates)
        {
            if (updates == null)
                return;
            foreach (var key in updates.Keys)
            {
                if (Variables.ContainsKey(key))
                    Variables[key] = updates[key];
                else
                    Variables.Add(key, updates[key]);
            }
        }

        /// <summary>
        /// Replaces variables in a given string
        /// </summary>
        /// <param name="input">the input string</param>
        /// <param name="variables">the variables used to replace</param>
        /// <param name="stripMissing">if missing variables shouild be removed</param>
        /// <returns>the string with the variables replaced</returns>
        public string ReplaceVariables(string input, bool stripMissing = false) => VariablesHelper.ReplaceVariables(input, Variables, stripMissing);


        /// <summary>
        /// Gets a safe filename with any reserved characters removed or replaced
        /// </summary>
        /// <param name="fullFileName">the full filename of the file to make safe</param>
        /// <returns>the safe filename</returns>
        public FileInfo GetSafeName(string fullFileName)
        {
            var dest = new FileInfo(fullFileName);

            string destName = dest.Name;
            string destDir = dest?.DirectoryName ?? "";

            // replace these here to avoid double spaces in name
            if (Path.GetInvalidFileNameChars().Contains(':'))
            {
                destName = destName.Replace(" : ", " - ");
                destName = destName.Replace(": ", " - ");
                destDir = destDir.Replace(" : ", " - ");
                destDir = destDir.Replace(": ", " - ");
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                if (c == ':')
                {
                    destName = destName.Replace(c.ToString(), " - ");
                    if(c != Path.DirectorySeparatorChar && c != Path.PathSeparator)
                        destDir = destDir.Replace(c.ToString(), " - ");
                }
                else
                {
                    destName = destName.Replace(c.ToString(), "");
                    if (c != Path.DirectorySeparatorChar && c != Path.PathSeparator)
                        destDir = destDir.Replace(c.ToString(), "");
                }
            }
            // put the drive letter back if it was replaced iwth a ' - '
            destDir = System.Text.RegularExpressions.Regex.Replace(destDir, @"^([a-z]) \- ", "$1:", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return new FileInfo(Path.Combine(destDir, destName));
        }

        public bool CreateDirectoryIfNotExists(string directory)
        {
            return Helpers.FileHelper.CreateDirectoryIfNotExists(Logger, directory);
        }

        /// <summary>
        /// Executes a cmd and returns the result
        /// </summary>
        /// <param name="args">The execution parameters</param>
        /// <returns>The result of the command</returns>
        public ProcessResult Execute(ExecuteArgs args)
        {
            var result = Process.ExecuteShellCommand(args).Result;
            return result;
        }

        /// <summary>
        /// Gets a new guid as a string
        /// </summary>
        /// <returns>a new guid as a string</returns>
        public string NewGuid() => Guid.NewGuid().ToString();
    }


    public enum NodeResult
    {
        Failure = 0,
        Success = 1,
    }
}