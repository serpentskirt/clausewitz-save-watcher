using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace clausewitz_save_watcher
{
    /// <summary>
    /// Backs up save game files.
    /// </summary>
    public class SaveWatcher
    {
        /// <summary>
        /// Indicates if save watcher is watching for file changes.
        /// </summary>
        private bool Running;

        /// <summary>
        /// Watcher to detect changes.
        /// </summary>
        private readonly FileSystemWatcher Watcher;

        /// <summary>
        /// Time in milliseconds for idle loop delay.
        /// </summary>
        private readonly uint IdleLoopDelay;

        /// <summary>
        /// Time in milliseconds to not react on same file events.
        /// </summary>
        private readonly uint FileEventDelay;

        /// <summary>
        /// String-represented path to directory to watch files in.
        /// </summary>
        private readonly string SourcePath;

        /// <summary>
        /// String-represented path to directory to copy files to.
        /// </summary>
        private readonly string TargetPath;

        /// <summary>
        /// Used to limit file events registration.
        /// </summary>
        private Timer FileEventLimiterTimer;

        /// <summary>
        /// Contains full paths to files to back up after file event delay is expired.
        /// </summary>
        private readonly List<string> Accumulator;

        /// <summary>
        /// Used in idler loop to limit CPU usage.
        /// </summary>
        private readonly EventWaitHandle WaitHandle;

        /// <summary>
        /// Synchronization mutex name.
        /// </summary>
        private const string MutexName = "FSW";

        /// <summary>
        /// Number of leading zeros in backed up file name.
        /// </summary>
        private const uint LeadingZeros = 4;

        /// <summary>
        /// Counter for copied files.
        /// </summary>
        private uint CopiedFiles;
        #region Events

        /// <summary>
        /// Event that occurs when change is detected by file watcher.
        /// </summary>
        /// <param name="source">The source of the event.</param>
        /// <param name="e">An object that contains event data.</param>
        private void OnChanged(object source, FileSystemEventArgs e)
        {
            Mutex mutex = new Mutex(false, MutexName);

            #region Exclusive access to Accumulator

            mutex.WaitOne();
            if (!File.GetAttributes(e.FullPath).HasFlag(FileAttributes.Directory))
                if (!Accumulator.Contains(e.FullPath))
                    Accumulator.Add(e.FullPath);
            mutex.ReleaseMutex();

            #endregion

            FileEventLimiterTimer.Change(FileEventDelay, Timeout.Infinite);
        }

        /// <summary>
        /// Backs up changed files when invoked from timer.
        /// </summary>
        /// <param name="state">An object containing application-specific information relevant to the method invoked by this delegate, or null.</param>
        private void ProcessChangedFiles(object state)
        {
            List<string> filesToProcess = new List<string>();
            Mutex mutex = new Mutex(false, MutexName);

            #region Exclusive access to Accumulator

            mutex.WaitOne();
            filesToProcess.AddRange(Accumulator);
            Accumulator.Clear();
            mutex.ReleaseMutex();

            #endregion

            foreach (string filePath in filesToProcess)
                CopyFile(filePath);
        }

        #endregion

        /// <summary>
        /// Watches for changes in source directory (and sub-directories) and copies changed files to target directory.
        /// </summary>
        /// <param name="sourcePath">String-represented path to directory to watch files in.</param>
        /// <param name="targetPath">String-represented path to directory to copy files to.</param>
        /// <param name="fileEventDelay">Time in milliseconds to not react on same file events.</param>
        /// <param name="idleLoopDelay">Time in milliseconds for idle loop delay.</param>
        /// <param name="filter">String mask for filtering watched files.</param>
        public SaveWatcher(string sourcePath, string targetPath, uint fileEventDelay = 200, uint idleLoopDelay = 2, string filter = "*.*")
        {
            CheckInputArguments(sourcePath, targetPath, filter);

            SourcePath = sourcePath;
            TargetPath = targetPath;
            IdleLoopDelay = idleLoopDelay;
            FileEventDelay = fileEventDelay;
            Running = false;
            Accumulator = new List<string>();
            FileEventLimiterTimer = null;
            CopiedFiles = 0;
            WaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, Guid.NewGuid().ToString());
            Watcher = new FileSystemWatcher
            {
                Path = SourcePath, 
                NotifyFilter = NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                Filter = filter
            };
        }
        
        /// <summary>
        /// Checks input arguments.
        /// </summary>
        /// <param name="sourcePath">Path to existing directory (source) to be checked.</param>
        /// <param name="targetPath">Path to existing directory (target) to be checked.</param>
        /// <param name="filter">String to filter file names by.</param>
        private void CheckInputArguments(string sourcePath, string targetPath, string filter)
        {
            if (String.Equals(sourcePath, targetPath))
                throw new ArgumentException("Source and target directories cannot be the same.");

            if (!Directory.Exists(sourcePath))
                throw new DirectoryNotFoundException(String.Concat(Path.GetFullPath(sourcePath), " is not existing."));

            if (!Directory.Exists(targetPath))
                throw new DirectoryNotFoundException(String.Concat(Path.GetFullPath(targetPath), " is not existing."));

            if (filter == null)
                throw new ArgumentNullException(nameof(filter));
        }

        /// <summary>
        /// Starts watching for changed files.
        /// </summary>
        public async Task Start()
        {
            Running = true;
            Watcher.Changed += new FileSystemEventHandler(OnChanged);
            Watcher.EnableRaisingEvents = true;

            if (FileEventLimiterTimer == null)
                FileEventLimiterTimer = new Timer(new TimerCallback(ProcessChangedFiles), null, Timeout.Infinite, Timeout.Infinite);

            do
                await WaitHandle.WaitOneAsync(TimeSpan.FromMilliseconds(IdleLoopDelay));
            while (Running);
        }

        /// <summary>
        ///     Stops watching for changing files.
        /// </summary>
        public void Stop()
        {
            if (Running)
            {
                Watcher.Changed -= new FileSystemEventHandler(OnChanged);
                Watcher.EnableRaisingEvents = false;
                FileEventLimiterTimer = null;
                Accumulator.Clear();
                Running = false;
            }
        }

        /// <summary>
        /// Copies file to target directory.
        /// </summary>
        /// <param name="filePath">Path to backed up file.</param>
        private void CopyFile(string filePath)
        {
            try
            {
                string newFileName = ConstructNewFileName(filePath);
                File.Copy(filePath, newFileName);
                Console.WriteLine("Backed up save game: {0}", newFileName);
                ++CopiedFiles;
            }
            catch (Exception ex)
            {
                Console.WriteLine(String.Concat(ex.GetType().ToString(), " occurred: ", ex.Message));
            }
        }

        /// <summary>
        /// Constructs file name for backed up file.
        /// </summary>
        /// <param name="filePath">Path to backed up file.</param>
        /// <returns>File name with prefix.</returns>
        private string ConstructNewFileName(string filePath)
        {
            string newFileName = String.Concat(CopiedFiles.ToString("D" + LeadingZeros), "_", Path.GetFileName(filePath));
            return Path.Combine(TargetPath, newFileName);
        }
    }
}
