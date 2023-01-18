using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.Storage;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Shapes;

namespace DiscDoingsWPF
{
    //[Serializable()]
    public class BurnPoolManager
    {
        public List<FileProps> AllFiles { get; set; }
        public List<FileProps> AllFilesNotInBurnQueue { get; set; }
        private string? _lastError;
        public string ThisPool { get; set; } //The name of this pool
        public List<OneBurn> BurnQueue { get; set; } //Burns which are ready
        //public List<OneBurn> completedBurns { get; set; } //Burns which have been completed
        public HashTypes HashTypeUsed { get; set; }
        public MainWindow? MainWindow; //This will be used to get access to the debug string in MainWindow

        private object _lockObjectFiles; //used for locking asyncrhonous operations

        public const int _formatVersion = 4;
        //formatVersion 1 adds the hashTypes enum, the hashType field to FileProps,
        //the HashTypeUsed field to BurnPoolManager, the createHashMD5 function, and this field indicating the format version.
        //Version 2: Added "timesBurned" field to OneBurn and remoived the "List<OneBurn> completedBurns" field
        //Version 3: Added "public errorCode fileStatus" to FileProps, to keep track of this file's standing relative to the example in the file system
        //Version 4: Added "overrideErrorCode" to FileProps

        [NonSerialized()] public bool TasksPendingFlag;
        

        public enum HashTypes
        {
            MD5,
            SHA256
        }

        public enum ErrorCode
        {
            FILES_EQUAL,
            FILE_NOT_FOUND_IN_FILESYSTEM,
            FILE_NOT_FOUND_IN_ARRAY,
            CHECKSUMS_DIFFERENT,
            ONEBURN_AUDIT_FAILED,
            SUCCESS,
            FAILED,
            CANCELED
        }

        //the get; set; properties do a lot of stuff but in this case they're necessary for serialization

        //Properties of a single file
        public struct FileProps
        {
            public string FileName { get; set; }
            public string OriginalPath{get; set;}
            public List<string> DiscsBurned { get; set; } //A list of every disc that this has been burned to
            public long Size { get; set; } //Size in bytes
            public byte[] Checksum { get; set; } //Checksum of this file
            public string TimeAdded { get; set; } //Date and time added to this pool
            public HashTypes HashType { get; set; }
            public ErrorCode FileStatus { get; set; }

            public string LastModified { get; set; } //Last modified time from file system
            public bool IsExtra { get; set; } //Set this flag to true if this file is intentionally meant to be burned to more than one disc
            public bool OverrideErrorCode { get; set; } //Override a bad error code and allow this file to be burned anyway
        }

        //Describes the contents that will go into one single burn
        public class OneBurn
        {
            public long SizeOfVolume { get; set; }
            public long SpaceRemaining { get; set; }
            public long SpaceUsed { get; set; }
            public List<FileProps> Files { get; set; } //Files to be burned. Do not alter this directly!
            public string Name { get; set; }
            public int TimesBurned { get; set; } //The number of times this OneBurn has been burned to a physical disc


            public OneBurn(long fileSizeBytes)
            {
                Files = new List<FileProps>();
                SpaceRemaining = fileSizeBytes;
                SizeOfVolume = fileSizeBytes;
                Name = "Untitled OneBurn";
                TimesBurned = 0;
            }

            public OneBurn() //Including a parameter-less constructor is necessary to use JsonSerializer
            {
                Files = new List<FileProps>();
                SpaceRemaining = 0;
                SizeOfVolume = 0;
                Name = "Untitled OneBurn";
                TimesBurned = 0;
            }

            //Calculates the value for spaceUsed and spaceRemaining
            public void FindSpaceRemaining()
            {
                SpaceRemaining = 0;
                SpaceUsed = 0;
                for (int i = 0; i < Files.Count; i++)
                {
                    SpaceUsed += Files[i].Size;
                }
                SpaceRemaining = SizeOfVolume - SpaceUsed;
            }



            //Returns false if the file is too big for the current instance

            public bool AddFile(FileProps file)
            {
                if (CanAddFile(file)) {
                    Files.Add(file);
                    FindSpaceRemaining();
                    SortFilesBySize();
                    return true;
                }
                return false;
            }

            //Pass an index in (files) and it will remove that file and adjust other values properly
            public bool RemoveFile(int index)
            {
                if (!Files.Remove(Files[index]))
                {
                    return false;
                }

                FindSpaceRemaining();
                return true;
            }

            //Determines whether there's enough space in this instance to add another file
            public bool CanAddFile(FileProps file)
            {
                if (file.Size > SpaceRemaining) return false;
                return true;
            }

            public void SortFilesBySize()
            {
                FileProps temphold;

                if (Files.Count < 2) return;

                for (int i = 0; i < Files.Count - 1; i++)
                {
                    if (Files[i + 1].Size > Files[i].Size)
                    {
                        temphold = Files[i];
                        Files[i] = Files[i + 1];
                        Files[i + 1] = temphold;
                        i = -1;
                    }
                }
            }

            public bool SetName(string newName)
            {
                Name = newName;

                return true;
            }

            //Pass the full path of a file. If that file exists in this OneBurn, it returns the index. If not, it returns -1
            public int FindFileByFullPath(string path)
            {
                const bool msgBox = false;
                const string debugName = "BurnPoolManager::OneBurn::findFileByFullPath():";
                for (int i = 0; i < Files.Count; i++)
                {
                    if (msgBox)
                    {
                        System.Windows.MessageBox.Show(debugName + "Comparing " + Files[i].OriginalPath + " with " + path);
                    }
                    if (Files[i].OriginalPath == path) return i;
                    if (msgBox) System.Windows.MessageBox.Show(debugName + "Did not match");
                }
                if (msgBox) System.Windows.MessageBox.Show(debugName + "No matches found");
                return -1;
            }

            new public string ToString()
            {
                const string debugName = "BurnPoolManager::OneBurn::formatToString():";
                const string OneBurnExportHeader = "#OneBurnName\n\n";
                const string OneBurnExportFormatting = "#FileName\n#OriginalPath\nSize:#SizeInBytes\nMD5 Checksum:#Checksum\nAdded to this burn on:#TimeAdded\nLast modified on:#LastModified\n\n";

                string results = OneBurnExportHeader.Replace("#OneBurnName", this.Name);

                for (int i = 0; i < this.Files.Count; i++)
                {
                    string oneFile = OneBurnExportFormatting;
                    oneFile = oneFile.Replace("#FileName", this.Files[i].FileName);
                    oneFile = oneFile.Replace("#OriginalPath", this.Files[i].OriginalPath);
                    oneFile = oneFile.Replace("#SizeInBytes", this.Files[i].Size.ToString());
                    oneFile = oneFile.Replace("#Checksum", BurnPoolManager.ChecksumToString(this.Files[i].Checksum));
                    oneFile = oneFile.Replace("#TimeAdded", this.Files[i].TimeAdded);
                    oneFile = oneFile.Replace("#LastModified", this.Files[i].LastModified);
                    results += oneFile;
                }

                return results;
            }

            public void Print()
            {
                Console.WriteLine("Printing contents of " + Name);
                for (int i = 0; i < Files.Count; i++)
                {
                    Console.WriteLine(Files[i].Size + " " + Files[i].FileName);
                }
            }

        }


        public BurnPoolManager()
        {
            AllFiles = new List<FileProps>();
            AllFilesNotInBurnQueue = new List<FileProps>();
            BurnQueue = new List<OneBurn>();
            //completedBurns = new List<OneBurn>();
            ThisPool = "Untitled Disc";
            _lastError = "";
            HashTypeUsed = HashTypes.MD5;
            MainWindow = null;
            TasksPendingFlag = false;
            _lockObjectFiles = new object();
        }


        public BurnPoolManager(MainWindow programMainWindow)
        {
            AllFiles = new List<FileProps>();
            AllFilesNotInBurnQueue = new List<FileProps>();
            BurnQueue = new List<OneBurn>();
            //completedBurns = new List<OneBurn>();
            ThisPool = "Untitled Disc";
            _lastError = "";
            HashTypeUsed = HashTypes.MD5;
            MainWindow = programMainWindow;
            TasksPendingFlag = false;
            _lockObjectFiles = new object();
        }


        //Copy constructor
        public BurnPoolManager(BurnPoolManager copySource)
        {
            const string debugName = "BurnPoolManager(BurnPoolManager copySource) (copy function):";
            //this.allFiles = copySource.allFiles;
            
            if (copySource.AllFiles == null) this.AllFiles = new List<FileProps>();
            else
            {
                this.AllFiles = new List<FileProps>();
                for (int i = 0; i < copySource.AllFiles.Count; i++)
                {
                    this.AllFiles.Add(copySource.AllFiles[i]);
                }
            }

            if (copySource.AllFilesNotInBurnQueue == null) this.AllFilesNotInBurnQueue = new List<FileProps>();
            else
            {
                this.AllFilesNotInBurnQueue = new List<FileProps>();
                for (int i = 0; i < copySource.AllFilesNotInBurnQueue.Count; i++)
                {
                    this.AllFilesNotInBurnQueue.Add(copySource.AllFilesNotInBurnQueue[i]);
                }
            }

            //this.burnQueue = copySource.burnQueue;
            if (copySource.BurnQueue == null) this.BurnQueue = new List<OneBurn>();
            else
            {
                this.BurnQueue = new List<OneBurn>();
                for (int i = 0; i < copySource.BurnQueue.Count; i++)
                {
                    this.BurnQueue.Add(copySource.BurnQueue[i]);
                }
            }


            if (copySource.HashTypeUsed == null)
            {
                //If there is no hashTypeUsed then the type is most likely SHA256, since that was what was used before MD5 was made default
                this.HashTypeUsed = HashTypes.SHA256;
                _EchoDebug(debugName + "No hash type was found in the source file. Assuming SHA256.");
            }
            else
            {
                this.HashTypeUsed = copySource.HashTypeUsed;
            }

            this.ThisPool = copySource.ThisPool;
            this._lastError = copySource._lastError;
            this.MainWindow = copySource.MainWindow;
            this.TasksPendingFlag = false;
        }
        

        public static bool operator ==(BurnPoolManager a, BurnPoolManager b)
        {
            string aSerialized = JsonSerializer.Serialize(a);
            string bSerialized = JsonSerializer.Serialize(b);
            if (aSerialized == bSerialized) return true;
            return false;
        }

        public static bool operator !=(BurnPoolManager a, BurnPoolManager b)
        {
            string aSerialized = JsonSerializer.Serialize(a), bSerialized = JsonSerializer.Serialize(b);
            if (a != b) return true;
            return false;
        }


        //Asyncrhonously adds a file to the burnpool. Excepts if storageFileIn is null or does not appear to be a valid path
        public Task AddFileAsync(StorageFile storageFileIn)
        {
            const bool debugVerbose = true;

            const string validPath = @"(^\\{2}.+\\(.)+[^\\])|(([a-z]|[A-Z])+:\\.+[^\\])",
                debugName = "addFileAsync:";

            Regex compare = new Regex(validPath);

            if (!storageFileIn.IsAvailable)
            {
                _EchoDebug(debugName + " error: storageFileIn.IsAvailable = false for this file: " + storageFileIn.Path.ToString() + "\n");
                throw new Exception(debugName + "storageFileIn.IsAvailable = false for this file: " + storageFileIn.Path.ToString());
                return null;
            }
            if (storageFileIn == null)
            {
                _EchoDebug(debugName + " error: storageFileIn == null");
                throw new NullReferenceException(debugName + " error: storageFileIn == null");
                return null;
            }

            if (!compare.IsMatch(storageFileIn.Path.ToString()))
            {
                _EchoDebug("addFile(StorageFile) error: File does not appear to be a valid system or network path with " +
                    "path and extension: " + storageFileIn.Path.ToString() + "\n");
                throw new Exception(debugName + "error: File does not appear to be a valid system or network path with " +
                    "path and extension: " + storageFileIn.Path.ToString());
                return null;
            }


            FileInfo fInfo = new FileInfo(storageFileIn.Path);


            string path = fInfo.FullName;

            //This used to return Task.Run and has been modified so the task doesn't start immediately
            return new Task(() => {
                FileProps newFile = new FileProps();
                newFile.FileName = GetFilenameFromPath(path);
                newFile.OriginalPath = path;
                newFile.Size = fInfo.Length;
                newFile.TimeAdded = DateTime.Now.ToString();
                newFile.LastModified = fInfo.LastWriteTime.ToString();
                newFile.IsExtra = false;
                newFile.DiscsBurned = new List<String>();
                newFile.FileStatus = ErrorCode.FILES_EQUAL;
                newFile.OverrideErrorCode = false;

                string hashString = "";
                byte[] hashtime = CreateHash(fInfo);
                for (int i = 0; i < hashtime.Length; i++)
                {
                    hashString += hashtime[i].ToString();
                }
                newFile.Checksum = hashtime;

                if (false) //disable dupe check
                {
                    lock (_lockObjectFiles)
                    {
                        for (int i = 0; i < AllFiles.Count; i++)
                        {
                            if (AllFiles[i].OriginalPath == newFile.OriginalPath)
                            {
                                //Do something that tells the user this file is a duplicate
                                _EchoDebug(debugName + "Duplicates found: [" + AllFiles[i].OriginalPath + "] and [" + newFile.OriginalPath + "]");
                                return;
                            }
                        }
                    }
                }

                lock (_lockObjectFiles)
                {
                    AllFiles.Add(newFile);
                    AllFilesNotInBurnQueue.Add(newFile);
                }
            });

        }


        //Adds a batch of files using Parallel.For
        public ErrorCode AddFilesConcurrently(IReadOnlyList<IStorageItem> files)
        {
            if (files == null || files.Count < 1) return ErrorCode.FAILED;

            Parallel.For(0, files.Count, index =>
            {
                FileInfo fInfo = new FileInfo(files[index].Path);
                string path = fInfo.FullName;

                FileProps newFile = new FileProps();
                newFile.FileName = GetFilenameFromPath(path);
                newFile.OriginalPath = path;
                newFile.Size = fInfo.Length;
                newFile.TimeAdded = DateTime.Now.ToString();
                newFile.LastModified = fInfo.LastWriteTime.ToString();
                newFile.IsExtra = false;
                newFile.DiscsBurned = new List<String>();
                newFile.FileStatus = ErrorCode.FILES_EQUAL;
                newFile.OverrideErrorCode = false;

                string hashString = "";
                byte[] hashtime = CreateHash(fInfo);
                for (int i = 0; i < hashtime.Length; i++)
                {
                    hashString += hashtime[i].ToString();
                }
                newFile.Checksum = hashtime;

                lock (_lockObjectFiles)
                {
                    AllFiles.Add(newFile);
                    AllFilesNotInBurnQueue.Add(newFile);
                }
            });

            return ErrorCode.SUCCESS;
        }

        //Don't make this async, it will probably cause problems
        //Returns false if there is an issue
        public bool RemoveFile(string fullPath)
        {
            const string debugName = "BurnPoolManager::removeFile():";
            //Need to remove it from the main list and check the burn queue for that file
            FileProps fileToRemove = AllFiles[FindFileByFullPath(fullPath)];

            if (fileToRemove.DiscsBurned.Count > 0)
            {
                _EchoDebug(debugName + "File \"" + fileToRemove.OriginalPath + "\" has been marked as burned. To remove it, first " +
                    "delete all discs that it has been burned to.");
                return false;
            }

            lock (_lockObjectFiles)
            {
                if (!AllFiles.Remove(fileToRemove))
                {
                    _EchoDebug(debugName + "File " + fullPath + " was not found in this BurnPoolManager.");
                    return false;
                }
            }

            
            AllFilesNotInBurnQueue.Remove(fileToRemove).ToString();

            //Remove it from all OneBurns with that file
            for (int i = 0; i < BurnQueue.Count; i++)
            {
                for (int x = 0; x < BurnQueue[i].Files.Count; x++)
                {
                    if (FilePropsEqual(BurnQueue[i].Files[x], fileToRemove))
                    {
                        BurnQueue[i].Files.RemoveAt(x);
                        x -= 1; 
                    }
                }
            }
            return true;
        }


        //Use this any time a file is removed from a OneBurn. This is necessary in order to update allFilesNotInBurnQueue
        //This will place the file back in allFilesNotInBurnQueue at the end, so run sortFilesBySize() after calling it
        public bool RemoveFileFromOneBurn(int oneBurnIndex, int fileIndex)
        {
            const string debugName = "BurnPoolManager::removeFileFromOneBurn():";
            const bool debug = false;

            if (debug)
            {
                _EchoDebug(debugName + "Starting with burnQueue: " + oneBurnIndex + " fileIndex: " + fileIndex);
            }

            FileProps fileToRemove = BurnQueue[oneBurnIndex].Files[fileIndex];

            if (oneBurnIndex > BurnQueue.Count || oneBurnIndex < 0)
            {
                _EchoDebug(debugName + "OneBurn index is out of range.");
                return false;
            }
            if (BurnQueue[oneBurnIndex].Files.Count < fileIndex || fileIndex < 0)
            {
                _EchoDebug(debugName + "File index is out of range.");
                return false;
            }

            if (BurnQueue[oneBurnIndex].TimesBurned > 0)
            {
                try
                {
                    MainWindow.InformUser(MainWindow.UserMessages.DISC_ALREADY_BURNED);
                    return false;
                }
                catch(NullReferenceException)
                {
                    _EchoDebug(debugName + "MainWindow reference is null.");
                }
            }

            try
            {
                if (!BurnQueue[oneBurnIndex].RemoveFile(fileIndex))
                {
                    _EchoDebug(debugName + "Attempt to remove file[" + fileIndex + "] in OneBurn[" + oneBurnIndex + "] " +
                        "was unsuccessful.");
                    return false;
                }
                AllFilesNotInBurnQueue.Add(fileToRemove);
                AllFiles[FindFileByFullPath(fileToRemove.OriginalPath)].DiscsBurned.Remove(BurnQueue[oneBurnIndex].Name);
                return true;

            }
            catch (NullReferenceException)
            {
                _EchoDebug(debugName + "Attempt to remove file[" + fileIndex + "] in OneBurn[" + oneBurnIndex + "] " +
                        "resulted in a null reference and was unsuccessful.");
                return false;
            }

            _EchoDebug(debugName + "Exited without successfully removing file or throwing a known error.");
            return false;
        }


        //Deletes a OneBurn from the list, restoring all files which are not in other OneBurns to the allFilesNotInBurnQueue
        //Make sure to check that said files aren't in other OneBurns, since that's possible to do.
        //This function will -not- check the timesBurned field to see if the OneBurn has been burned to a disc.
        //The calling function should make any compensations for that on its own!
        public bool DeleteOneBurn(int oneBurnIndex)
        {
            const string debugName = "BurnPoolManager::deleteOneBurn:", friendlyName = debugName;
            const bool debug = false;

            
            if (oneBurnIndex > BurnQueue.Count || oneBurnIndex < 0)
            {
                _EchoDebug(debugName + "Invalid OneBurn index.");
                return false;
            }

            if (debug) _EchoDebug(debugName + "Start. Number of files to remove: " + BurnQueue[oneBurnIndex].Files.Count);


            FileProps temphold = new FileProps();
            for (int i = 0; i < BurnQueue[oneBurnIndex].Files.Count; i++)
            {
                temphold = BurnQueue[oneBurnIndex].Files[i];

                AllFilesNotInBurnQueue.Add(temphold);
                

                int indexInMainList = FindFileByFullPath(temphold.OriginalPath);
                for (int x = 0; x < AllFiles[indexInMainList].DiscsBurned.Count; x++)
                {
                    if (AllFiles[indexInMainList].DiscsBurned[x] == BurnQueue[oneBurnIndex].Name)
                    {
                        AllFiles[indexInMainList].DiscsBurned.RemoveAt(x);
                    }
                }
            }

            BurnQueue.RemoveAt(oneBurnIndex);
            SortFilesBySize();

            return true;
        }


        public byte[] CreateHash(FileInfo fInfo)
        {
            const string debugName = "createHash:";
            if (HashTypeUsed == HashTypes.SHA256) return (CreateHashSHA256(fInfo));
            if (HashTypeUsed == HashTypes.MD5) return (CreateHashMD5(fInfo));

            _EchoDebug(debugName + "Checksum error: hashTypeUsed appears to be invalid: " + HashTypeUsed);
            return new byte[0];
        }


        public byte[] CreateHashSHA256(FileInfo fInfo)
        {
            const string debugName = "createHashSHA256:";
            const bool debug = true;
            if (debug) _EchoDebugA(debugName + "Start");
            //do the hashing
            using (SHA256 hashtime = SHA256.Create())
            {
                try
                {
                    using (FileStream dataToHash = fInfo.Open(FileMode.Open))
                    {
                        byte[] hashValue = hashtime.ComputeHash(dataToHash);
                        if (debug) _EchoDebugA(debugName + "Returning");
                        return hashValue;

                    }
                }
                catch (IOException exception)
                {
                    _EchoDebug(debugName + "Exception thrown:" + exception);

                }
                catch (UnauthorizedAccessException exception)
                {
                    _EchoDebug(debugName + "Exception thrown:" + exception);
                }
            }

            _EchoDebug(debugName + "Checksum error: Exited using block without returning a value.");
            return new byte[0];

        }

        public byte[] CreateHashMD5(FileInfo fInfo)
        {
            const string debugName = "createHashMD5:";
            const bool debug = false;
            //do the hashing

            if (debug) _EchoDebugA(debugName + "Start");

            using (MD5 hashtime = MD5.Create())
            {
                try
                {
                    using (FileStream dataToHash = fInfo.Open(FileMode.Open))
                    {
                        byte[] hashValue = hashtime.ComputeHash(dataToHash);
                        if (debug) _EchoDebugA(debugName + "Returning");
                        return hashValue;

                    }
                }
                catch (IOException exception)
                {
                    _EchoDebug(debugName + "Exception thrown:" + exception);

                }
                catch (UnauthorizedAccessException exception)
                {
                    _EchoDebug(debugName + "Exception thrown:" + exception);
                }
            }

            _EchoDebug(debugName + "Checksum error: Exited using block without returning a value.");
            return new byte[0];

        }


        //Compare two byte arrays. For comparing checksums
        public static bool ByteArrayEqual(byte[] arr1, byte[] arr2)
        {
            if (arr1.Length == 0 && arr2.Length == 0) return true;
            if (arr1.Length != arr2.Length) return false;

            for (int i = 0; i < arr1.Length; i++)
            {
                if (arr1[i] != arr2[i]) return false;
            }
            return true;
        }


        //Check if two FileProps instances are equal. Ignores the isExtra flag and discs burned list
        public static bool FilePropsEqual(FileProps a, FileProps b)
        {
            if (a.FileName != b.FileName) return false;
            if (a.OriginalPath != b.OriginalPath) return false;
            if (a.LastModified != b.LastModified) return false;
            if (a.Size != b.Size) return false;
            if (a.TimeAdded != b.TimeAdded) return false;
            if (!ByteArrayEqual(a.Checksum, b.Checksum)) return false;
            return true;
        }

        public static bool FilePropsEqual(FileProps a, FileProps b, bool ignoreChecksum)
        {
            if (a.FileName != b.FileName) return false;
            if (a.OriginalPath != b.OriginalPath) return false;
            if (a.LastModified != b.LastModified) return false;
            if (a.Size != b.Size) return false;
            if (a.TimeAdded != b.TimeAdded) return false;
            if (!ignoreChecksum)
            {
                if (!ByteArrayEqual(a.Checksum, b.Checksum)) return false;
            }
            return true;
        }

        //Pass an index in allFiles. It will find the same file in the file system, calculate a fresh checksum of the file,
        //and return whether there is a match
        public ErrorCode CompareChecksumToFileSystem(int allFilesIndex)
        {
            const string debugName = "BurnPoolManager::compareChecksumToFileSystem():", friendlyName = debugName;
            const bool debug = false, log = true;
            

            if (allFilesIndex < 0 || allFilesIndex > AllFiles.Count)
            {
                _LogOutput(friendlyName + "File at index " + allFilesIndex + " was not found in this database with a range of " + AllFiles.Count);
                return ErrorCode.FILE_NOT_FOUND_IN_ARRAY;
            }

            FileInfo fInfo = new FileInfo(AllFiles[allFilesIndex].OriginalPath);

            if (!fInfo.Exists)
            {
                _LogOutput(friendlyName + "The file \"" + AllFiles[allFilesIndex].OriginalPath + "\" was not found in " +
                    "its original location, or has been renamed.");
                return ErrorCode.FILE_NOT_FOUND_IN_FILESYSTEM;
            }

            

            byte[] checksum = CreateHash(fInfo);

            if (ByteArrayEqual(checksum, AllFiles[allFilesIndex].Checksum))
            {
                if (debug) _EchoDebug(friendlyName + "The file \"" + AllFiles[allFilesIndex].OriginalPath + "\" appears to be OK.");


                return ErrorCode.FILES_EQUAL;
            }
            _LogOutput(friendlyName + "The file \"" + AllFiles[allFilesIndex].OriginalPath + "\" does not match the checksum made when the file was added.");
            return ErrorCode.CHECKSUMS_DIFFERENT;
        }

        public static string ChecksumToString(byte[] checksum)
        {
            string result = "";
            for (int i = 0; i < checksum.Length; i++)
            {
                result += checksum[i].ToString();
            }
            return result;
        }

        //Recalculate the checksum for a file based off the example in the file system. Returns false if there is an error.
        public bool RecalculateChecksum(int allFilesIndex)
        {
            const string debugName = "BurnPoolManager::recalculateChecksum():";
            const bool debug = true;

            if (debug) _EchoDebug(debugName + "Start");
            
            if (allFilesIndex < 0 || allFilesIndex > AllFiles.Count)
            {
                _EchoDebug(debugName + "Passed index of " + allFilesIndex + " is out of range.");
                return false;
            }

            FileProps replacementFile = AllFiles[allFilesIndex];

            FileInfo fInfo = new FileInfo(replacementFile.OriginalPath);

            if (!fInfo.Exists)
            {
                _EchoDebug(debugName + "The file [" + replacementFile.OriginalPath + "] does not exist.");
                return false;
            }

            replacementFile.Checksum = CreateHash(fInfo);
            replacementFile.FileStatus = ErrorCode.FILES_EQUAL;
            AllFiles[allFilesIndex] = replacementFile;

            ReplaceChecksum(allFilesIndex, replacementFile.Checksum);
            SetErrorCode(allFilesIndex, ErrorCode.FILES_EQUAL);

            if (debug) _EchoDebug(debugName + "Complete");
            return true;

        }

        //Use this when changing the error code on a file to not only update the file, but update all examples of that
        //file in the burn queue.
        public bool SetErrorCode(int allFilesIndex, ErrorCode codeToSet)
        {
            const string debugName = "BurnPoolManager::setErrorCode():";
            const bool debug = false, logging = true;

            if (debug) _EchoDebug(debugName + "Start");
            if (allFilesIndex > AllFiles.Count || allFilesIndex < 0)
            {
                _EchoDebug(debugName + "Index of " + allFilesIndex + " is out of the range of " + AllFiles.Count);
                return false;
            }

            FileProps replacementFile = AllFiles[allFilesIndex];
            replacementFile.FileStatus = codeToSet;
            AllFiles[allFilesIndex] = replacementFile;

            for (int i = 0; i < BurnQueue.Count; i++)
            {
                for (int x = 0; x < BurnQueue[i].Files.Count; x++)
                {
                    //Ignore the checksum, as we may use this function to set error codes indicating inequal checksums
                    if (FilePropsEqual(BurnQueue[i].Files[x], replacementFile, true) && BurnQueue[i].TimesBurned == 0)
                    {
                        lock (_lockObjectFiles)
                        {
                            BurnQueue[i].Files[x] = replacementFile;
                        }
                    }
                }
            }

            if (debug) _EchoDebug(debugName + "End");
            return true;
        }

        //Replace the checksum in a file with the one passed. Also replaces the checksum in all examples of that file in the burn queue.
        //This doesn't calculate a checksum; use recalculateChecksum() for that
        public bool ReplaceChecksum(int allFilesIndex, byte[] newChecksum)
        {
            const string debugName = "BurnPoolManager::replaceChecksum():";
            const bool debug = false;

            if (debug) _EchoDebug(debugName + "Start");

            FileProps replacementFile = AllFiles[allFilesIndex];
            replacementFile.Checksum = newChecksum;
            AllFiles[allFilesIndex] = replacementFile;

            for (int i = 0; i < BurnQueue.Count; i++)
            {
                for (int x = 0; x < BurnQueue[i].Files.Count; x++)
                {
                    if (FilePropsEqual(BurnQueue[i].Files[x], replacementFile, true) && BurnQueue[i].TimesBurned == 0)
                    {
                        BurnQueue[i].Files[x] = replacementFile;
                    }
                }
            }

            if (debug) _EchoDebug(debugName + "End");
            return true;
        }

                                            //Begin burn organizing related code

        //Checks if a file is already in the burn queue list. Returns false if either the file being added or a found match have the
        //isExtra flag set to true.
        private bool _ExistsInBurnQueue(FileProps file)
        {
            bool debug = false;
            if (debug) _EchoDebug("existsInBurnQueue start: " + file.FileName);
            if (file.IsExtra == true)
            {
                if (debug) _EchoDebug("existsInBurnQueue: isExtra = true, returning false");
                return false;
            }
            for (int i = 0; i < BurnQueue.Count; i++)
            {
                for (int x = 0; x < BurnQueue[i].Files.Count; x++)
                {
                    if (FilePropsEqual(BurnQueue[i].Files[x], file) &&
                        BurnQueue[i].Files[x].IsExtra == false){
                        if (debug) _EchoDebug("existsInBurnQueue: File found in burn queue, returning true");
                        return true;
                        }
                }
            }
            if (debug) _EchoDebug("existsInBurnQueue: File not found in burn queue, returning false");
            return false;
        }

        //Returns a list containing every file that is in the burnQueue instantiation
        private List<FileProps> _AllFilesInBurnQueue()
        {
            List<FileProps> combinedLists = new List<FileProps>();
            for (int i = 0; i < BurnQueue.Count; i++)
            {
                for (int x = 0; x < BurnQueue[i].Files.Count; x++)
                {
                    combinedLists.Add(BurnQueue[i].Files[x]);
                }
            }

            return combinedLists;
        }


        //Generate a OneBurn and add it to the queue. Returns false if a OneBurn can't be generated.
        public bool GenerateOneBurn(long sizeInBytes)
        {
            const bool verbose = false, debug = false;
            const string debugName = "generateOneBurn:";
            OneBurn aBurn = new OneBurn(sizeInBytes);
            aBurn.SetName(ThisPool + (BurnQueue.Count));

            if (debug || verbose) _EchoDebug(debugName + "Start. Total files in allFilesNotInBurnQueue: " + AllFilesNotInBurnQueue.Count);

            SortFilesBySize();

            if (verbose) _EchoDebug(debugName + "Files sorted by size");

            for (int i = 0; i < AllFilesNotInBurnQueue.Count; i++) //Start with the biggest file that will fit
            {
                if (aBurn.CanAddFile(AllFilesNotInBurnQueue[i]))
                {
                    aBurn.AddFile(AllFilesNotInBurnQueue[i]);
                    AllFilesNotInBurnQueue.RemoveAt(i);
                    break;
                }
            }

            long spaceTarget = aBurn.SpaceRemaining / 2;
            int passes = 0; //for debug purposes
            if (verbose) _EchoDebug(debugName + "First file added, loopy next");

            do
            {
                if (debug) _EchoDebug(debugName + "Files remaining in allFilesNotInBurnQueue: " + AllFilesNotInBurnQueue.Count);
                int? nextFile = FindFileByNearestSizeA(AllFilesNotInBurnQueue, spaceTarget, aBurn.SpaceRemaining);

                if (verbose)
                {
                    _EchoDebug(debugName + " pass " + passes + " nextFile:" + nextFile + " spaceTarget = " + spaceTarget);
                }

                if (nextFile == null) break; //No suitable files were found
                int nextFileInt = nextFile ?? default(int); //necessary to resolve the nullable int

                if (verbose)
                {
                    _EchoDebug(debugName + "Adding: " + AllFilesNotInBurnQueue[nextFileInt]);
                    passes++;
                    MainWindow.DebugEchoAsync(passes.ToString());
                    if (passes > 5000) MessageBox.Show("Hey");
                }
                aBurn.AddFile(AllFilesNotInBurnQueue[nextFileInt]);
                AllFilesNotInBurnQueue.RemoveAt(nextFileInt);
                //Note: findFileByNearestSize examines every file in the given List<FileProps> on each pass, and ignores any results that would
                //not fit the limit or that are already in the burn list. For that reason, no additional checks should be needed here as it
                //will return null if it can't find anything that fits these criteria

                spaceTarget = aBurn.SpaceRemaining / 2;
            }
            while (true || AllFilesNotInBurnQueue.Count > 0);
            if (aBurn.Files.Count > 0)
            {
                BurnQueue.Add(aBurn);
                if (verbose || debug) _EchoDebug(debugName + "aBurn generated, exiting " + debugName);
                return true;
            }
            else
            {
                if (verbose || debug) _EchoDebug(debugName + "aBurn was not generated, exiting " + debugName);
                return false;
            }
            
        }


        //Search the burnQueue list for a OneBurn with the passed filename. If it finds a result, it returns its position in the list.
        //If there is no result, it returns null.
        public int? GetBurnQueueFileByName(string oneBurnName)
        {
            const bool debug = false;
            const string debugName = "BurnPoolManager::getBurnQueueFileByName:";
            if (debug)
            {
                _EchoDebug(debugName + "Starting with oneBurnName: " + oneBurnName);
                _EchoDebug("Looking in these OneBurns:");
                for (int i = 0; i < BurnQueue.Count; i++)
                {
                    _EchoDebug(BurnQueue[i].Name);
                }
            }
            for (int i = 0; i < BurnQueue.Count; i++)
            {
                if (BurnQueue[i].Name == oneBurnName)
                {
                    if (debug)
                    {
                        _EchoDebug(debugName + "OneBurn \"" + BurnQueue[i].Name + "\" equals \"" + oneBurnName + "\" Returning " + i);
                    }
                    return i;
                }
            }

            if (debug)
            {
                _EchoDebug(debugName + "No equal results were found, returning null.");
            }
            return null;
        }

        //Sort the files in allFiles by size, with [0] being the largest.
        public void SortFilesBySize()
        {
            const bool debug = false;
            const string debugName = "sortFilesBySize:";
            FileProps temphold;
            int iterations = 0, restarts = 0;

            SortAllFilesNotInBurnQueueBySize();

            if (debug) _EchoDebug(debugName + "Start");

            if (AllFiles.Count < 2) return;

            for(int i = 0; i < AllFiles.Count - 1; i++)
            {
                if (debug)
                {
                    iterations++;
                    //if (iterations % 5000 == 0) echoDebug(iterations.ToString() + " " + restarts.ToString());
                }

                if(AllFiles[i + 1].Size > AllFiles[i].Size)
                {
                    if (debug)
                    {
                        if (restarts % 5000 == 0) _EchoDebug(restarts + ": " + AllFiles[i + 1].Size 
                            + " is larger than " + AllFiles[i].Size);
                    }
                    temphold = AllFiles[i];
                    AllFiles[i] = AllFiles[i + 1];
                    AllFiles[i + 1] = temphold;
                    
                    //Keep moving it up the list as long as it's bigger than the one before it
                    while (i > 0 && AllFiles[i].Size > AllFiles[i - 1].Size)
                    {
                        temphold = AllFiles[i - 1];
                        AllFiles[i - 1] = AllFiles[i];
                        AllFiles[i] = temphold;
                        i--;
                    }

                    i = -1; //Start the main loop over from the top


                    if (debug)
                    {
                        restarts++;
                    }
                }
            }
        }

        //Same as above, but works with a list passed as an argument. Modifies the list directly
        public void SortAllFilesNotInBurnQueueBySize()
        {
            const bool debug = false;
            const string debugName = "BurnPoolManager::sortFilesBySize(ref List<FileProps> listToSort):";
            FileProps temphold;
            int iterations = 0, restarts = 0;

            if (debug) _EchoDebug(debugName + "Start");

            if (AllFilesNotInBurnQueue.Count < 2) return;

            for (int i = 0; i < AllFilesNotInBurnQueue.Count - 1; i++)
            {
                if (debug)
                {
                    iterations++;
                    //if (iterations % 5000 == 0) echoDebug(iterations.ToString() + " " + restarts.ToString());
                }
                //Console.WriteLine(i + " About to compare " + allFiles[i].fileName + " " + allFiles[i].size + " to " +
                //allFiles[i + 1].fileName + " " + allFiles[i + 1].size);
                if (AllFilesNotInBurnQueue[i + 1].Size > AllFilesNotInBurnQueue[i].Size)
                {
                    if (debug)
                    {
                        if (restarts % 5000 == 0) _EchoDebug(restarts + ": " + AllFiles[i + 1].Size
                            + " is larger than " + AllFiles[i].Size);
                    }
                    temphold = AllFilesNotInBurnQueue[i];
                    AllFilesNotInBurnQueue[i] = AllFilesNotInBurnQueue[i + 1];
                    AllFilesNotInBurnQueue[i + 1] = temphold;

                    //Keep moving it up the list as long as it's bigger than the one before it
                    while (i > 0 && AllFilesNotInBurnQueue[i].Size > AllFilesNotInBurnQueue[i - 1].Size)
                    {
                        temphold = AllFilesNotInBurnQueue[i - 1];
                        AllFilesNotInBurnQueue[i - 1] = AllFilesNotInBurnQueue[i];
                        AllFilesNotInBurnQueue[i] = temphold;
                        i--;
                    }

                    i = -1; //Start the main loop over from the top


                    if (debug)
                    {
                        restarts++;
                    }
                }
            }
        }

        //Once a OneBurn has been burned, this commits it to the burned discs list
        public ErrorCode CommitOneBurn(int burnQueueMember, bool verifyFiles)
        {
            const string debugName = "BurnPoolManager::commitOneBurn:", friendlyName = debugName;
            const bool debug = false, verbose = false;

            if (burnQueueMember > BurnQueue.Count || burnQueueMember < 0){
                _EchoDebug(debugName + "Index of " + burnQueueMember + " is out of range.");
                return ErrorCode.FAILED;
            }
            if (BurnQueue[burnQueueMember] == null)
            {
                _EchoDebug(debugName + "burnQueue index " + burnQueueMember + " is null.");
                return ErrorCode.FAILED;
            }

            int verifyCount = BurnQueue[burnQueueMember].Files.Count;
            if (verifyFiles)
            {
                bool foundFaults = false;
                for (int i = 0; i < verifyCount; i++)
                {

                    if (verbose) _EchoDebug(debugName + "On file " + i + " of " + verifyCount);

                    if (TasksPendingFlag)
                    {
                        _EchoDebug(debugName + "Operation canceled by tasks in progress. Please wait for tasks to complete.");
                        return ErrorCode.CANCELED;
                    }

                    if (verifyCount != BurnQueue[burnQueueMember].Files.Count)
                    {
                        _EchoDebug(debugName + "The file count of burnQueue[" + burnQueueMember + "] named \"" + BurnQueue[burnQueueMember]
                            + " initiated at " + verifyCount + " and has changed to " + BurnQueue[burnQueueMember].Files.Count + ". Aborting operation.");
                        return ErrorCode.FAILED;
                    }
                    if (burnQueueMember > BurnQueue.Count)
                    {
                        _EchoDebug(debugName + "The passed burn queue member value of " + burnQueueMember + " now exceeds the burnQueue size of " + BurnQueue.Count +
                            ". Aborting operation.");
                        return ErrorCode.FAILED;
                    }

                    int fileInAllFiles = FindFileByFullPath(BurnQueue[burnQueueMember].Files[i].OriginalPath);
                    ErrorCode fileCheck = CompareChecksumToFileSystem(fileInAllFiles);
                    if (fileCheck != ErrorCode.FILES_EQUAL)
                    {
                        if (!SetErrorCode(fileInAllFiles, fileCheck))
                        {
                            _EchoDebug(debugName + "setErrorCode returned a value of false.");
                            return ErrorCode.FAILED;
                        }
                        if (fileInAllFiles > AllFiles.Count || fileInAllFiles < 0)
                        {
                            _EchoDebug(debugName + "The index of " + fileInAllFiles + " is no longer valid in allFiles, which has a range of " + AllFiles.Count);
                        }

                        if (!AllFiles[fileInAllFiles].OverrideErrorCode)
                        {
                            _LogOutput(friendlyName + "File \"" + AllFiles[fileInAllFiles].OriginalPath + "\" returned an error code of " + fileCheck.ToString());
                            foundFaults = true;
                        }
                        else
                        {
                            _LogOutput(friendlyName + "File \"" + AllFiles[fileInAllFiles].OriginalPath + "\" returned an error code of " + fileCheck.ToString() + ", but the override flag is set to true, so it will be committed anyway.");                                
                        }
                    }
                }
                if (foundFaults) return ErrorCode.ONEBURN_AUDIT_FAILED;

            }

            for (int i = 0; i < BurnQueue[burnQueueMember].Files.Count; i++)
            {
                AllFiles[FindFileByFullPath(BurnQueue[burnQueueMember].Files[i].OriginalPath)].DiscsBurned.Add(BurnQueue[burnQueueMember].Name);
            }

            BurnQueue[burnQueueMember].TimesBurned++;
            return ErrorCode.SUCCESS;
        }


        //If this member in the burnQueue has been marked as burned, unmark it and remove that OneBurn from the 
        //discsBurned field of all FileProps in allFiles
        public ErrorCode UncommitOneBurn(int burnQueueMember)
        {
            const string debugName = "BurnPoolManager::uncommitOneBurn():";
            const bool debug = false;

            if (burnQueueMember > BurnQueue.Count || burnQueueMember < 0)
            {
                _EchoDebug(debugName + "Index of " + burnQueueMember + " is out of range.");
                return ErrorCode.FAILED;
            }
            if (BurnQueue[burnQueueMember] == null)
            {
                _EchoDebug(debugName + "burnQueue index " + burnQueueMember + " is null.");
                return ErrorCode.FAILED;
            }

            if (BurnQueue[burnQueueMember].TimesBurned == 0)
            {
                _EchoDebug("burnQueue[" + burnQueueMember + "] has not been committed to a disc.");
                return ErrorCode.FAILED;
            }

            for (int i = 0; i < BurnQueue[burnQueueMember].Files.Count; i++)
            {
                if (TasksPendingFlag)
                {
                    _EchoDebug(debugName + "Operation canceled by tasks in progress. Please wait for tasks to complete.");
                    return ErrorCode.CANCELED;
                }
                int locationInAllFiles = FindFileByFullPath(BurnQueue[burnQueueMember].Files[i].OriginalPath);

                if (locationInAllFiles == -1)
                {
                    _EchoDebug(debugName + "The file \"" + BurnQueue[burnQueueMember].Files[i].OriginalPath + "\" was not found in " +
                        "allFiles. This may indicate corruption or that data was mishandled during file deletion or creation of " +
                        "this OneBurn.");
                    return ErrorCode.FAILED;
                }

                int thisBurnInFile = AllFiles[locationInAllFiles].DiscsBurned.IndexOf(BurnQueue[burnQueueMember].Name);
                if (thisBurnInFile > -1)
                {
                    AllFiles[locationInAllFiles].DiscsBurned.RemoveAt(thisBurnInFile);
                }
                else
                {
                    _EchoDebug(debugName + "The OneBurn titled \"" + BurnQueue[burnQueueMember].Name + "\" at burnQueue index " +
                        burnQueueMember + " did not appear in the file \"" + AllFiles[locationInAllFiles].OriginalPath +
                        "\". This may indicate corruption in the struct or an error in how this OneBurn was marked burned.");
                    return ErrorCode.FAILED;
                }
            }

            BurnQueue[burnQueueMember].TimesBurned--;
            return ErrorCode.SUCCESS;
        }


        //Pass a List<FileProps> and a size constraint and receive a new List<FileProps> that has been filled in with files from the
        //source list, beginning at the largest, that will fit within that limit. 'exclude' is files found in source that will not be
        //considered
        public List<FileProps>? GenerateListByFileSize(long sizeLimit, List<FileProps> source, List<FileProps> exclude)
        {
            bool debug = true;
            string debugName = "generateListByFileSize:";

            long totalSize = 0, runningTotalSize = 0;
            List<FileProps> complete = new List<FileProps>();
            if (sizeLimit == 0)
            {
                _Error("generateListByFileSize error: Size limit set to 0");
                return null;
            }
            if (source.Count == 0)
            {
                _Error("generateListByFileSize error: No files in List<FileProps> source");
                return null;
            }

            for (int i = 0; i < source.Count; i++)
            {
                totalSize += source[i].Size;
            }

            if (totalSize < sizeLimit)
            {
                _Error("generateListByFileSize: Source list is smaller than file size limit, returning source list");
                return source;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (debug)
                {
                    _EchoDebug(debugName + "Running total: " + runningTotalSize + " Size limit: " + sizeLimit + " File: " 
                        + source[i].FileName + " File size: " + source[i].Size);
                    _EchoDebug(debugName + "Filesize + running total: " + source[i].Size + runningTotalSize);
                }

                if (source[i].Size + runningTotalSize < sizeLimit)
                {
                    if (FindFile(source[i], exclude) == -1)
                    {
                        if (debug)
                        {
                            _EchoDebug(debugName + "The file " + source[i].FileName + " " + source[i].Size + " is not in the burn list already," +
                                "so it is added.");
                        }

                        complete.Add(source[i]);
                        runningTotalSize += source[i].Size;
                    }
                }
            }

            return complete;
        }


        //Pass a FileProps and a list, if there is an identical FileProps in that list, it will return its position.
        //Returns -1 if there is no match
        public int FindFile(FileProps file, List<FileProps> fileList)
        {
            for (int i = 0; i < fileList.Count; i++)
            {
                if (FilePropsEqual(fileList[i], file))
                {
                    return i;
                }
            }
            return -1;
        }

        //Pass a full path to a file and if that file exists in this burnpool, it will return its position.
        //Returns -1 if no match was found
        public int FindFileByFullPath(string path)
        {
            const string debugName = "BurnPoolManager::findFileByFullPath():";
            for (int i = 0; i < AllFiles.Count; i++)
            {
                if (AllFiles[i].OriginalPath == path) return i;
            }
            _EchoDebug(debugName + "A file with the full path of \"" + path + "\" was not found.");
            return -1;
        }
    
        //Pass a List<FileProps> and a desired file size, it will return the position in 'list' of the element whose file size is nearest
        //(higher or lower) to that size. If there is an equal difference between the next highest and lowest file, it will return the highest.
        //Expects the List<FileProps> to be ordered from largest-smallest (0 is largest)
        //Any files identical to files in 'ignore' or larger than 'maxSize' will not be considered. Returns null if no suitable files are found
        public int? FindFileByNearestSize(List<FileProps> list, long target, List<FileProps> ignore, long maxSize)
        {
            bool verbose = false;
            string debugName = "findFileByNearestSize:";
            int? favorite = null;
            Func<FileProps, List<FileProps>, long, bool> meetsConditions = (aFile, ignoreList, sizeLimit) =>
                (FindFile(aFile, ignoreList) == -1) && aFile.Size <= maxSize && (!_ExistsInBurnQueue(aFile));

            if (verbose)
            {
                _EchoDebug(debugName + "Starting with target " + target + " and maxSize " + maxSize);
            }

            for (int i = 0; i < list.Count; i++) //Start with the favorite set to the biggest file that meets criteria
            {
                if (meetsConditions(list[i], ignore, maxSize)) favorite = i;
            }

            if (favorite == null) return favorite;
            int favoriteInt = favorite ?? default(int);

            
            if (list[favoriteInt].Size <= target && meetsConditions(list[favoriteInt], ignore, maxSize))
            {
                if (verbose)
                {
                    _EchoDebug(debugName + "File " + list[favoriteInt].FileName + " with the size " + list[favoriteInt].Size 
                        + " is less than or equal to "
                        + target + " and meets conditions. Returning 0");
                }
                return favoriteInt;
            }
            

            if (list[favoriteInt].Size > target)
            {
                if (verbose)
                {
                    _EchoDebug(debugName + "Element 0: " + list[0].FileName + " with the size " + list[0].Size + " is larger than the target value of " 
                        + target);
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Size == target && meetsConditions(list[i], ignore, maxSize))
                    {
                        if (verbose)
                        {
                            _EchoDebug(debugName + list[i].FileName + " size " + list[i].Size + " matches the target of " + target);
                        }
                        return i;
                    }
                    if (list[i].Size > target && meetsConditions(list[i], ignore, maxSize))
                    {
                        if (verbose)
                        {
                            _EchoDebug(debugName + list[i].FileName + " size " + list[i].Size + " is larger than the target of " + target 
                                + " and the favorite is being set to this file.");
                        }
                        favorite = i;

                    }
                    if (list[i].Size < target && meetsConditions(list[i], ignore, maxSize))
                    {
                        if (target - list[i].Size == favorite - target)
                        {
                            if (verbose)
                            {
                                _EchoDebug(debugName + list[i].FileName + " size " + list[i].Size + " is less than the target of " + target
                                    + " and has an equal distance from the target as the previous favorite. Returning the previous (larger) favorite.");
                            }
                            return favorite;
                        }
                        if (target - list[i].Size < favorite - target)
                        {
                            if (verbose)
                            {
                                _EchoDebug(debugName + list[i].FileName + " size " + list[i].Size + " is less than the target of " + target
                                    + "and the difference is closer than the difference between the target and previous favorite. Returning this file.");
                            }
                            return i;
                        }
                        if (verbose)
                        {
                            int a = favorite ?? default(int);
                            _EchoDebug(debugName + list[i].FileName + " size " + list[i].Size + " is less than the target of " + target
                                + " but is a greater difference from the target than the previous favorite. Returning the favorite value of: " 
                                + favorite + " which is " + list[a] + " " + list[a].Size);
                        }
                        return favorite;
                    }
                }
            }

            if (verbose)
            {
                _EchoDebug(debugName + "The loop exited without returning a value. Returning " + favorite);
            }

            return favorite;


        }

        //Modify to no longer utilize the ignore list or check if a file is in the burn queue, only searching the List<FileProps> list
        //for the nearest file
        public int? FindFileByNearestSizeA(List<FileProps> list, long target, long maxSize)
        {
            bool verbose = false;
            string debugName = "findFileByNearestSizeA:";
            int? favorite = null;
            Func<FileProps, long, bool> meetsConditions = (aFile, sizeLimit) => (aFile.Size <= maxSize);

            if (verbose)
            {
                _EchoDebug(debugName + "Starting with target " + target + " and maxSize " + maxSize);
            }

            for (int i = 0; i < list.Count; i++) //Set the favorite to the biggest file that meets criteria
            {
                if (meetsConditions(list[i], maxSize)) favorite = i;
            }

            if (favorite == null) return favorite;
            int favoriteInt = favorite ?? default(int);


            if (list[favoriteInt].Size <= target && meetsConditions(list[favoriteInt], maxSize))
            {
                if (verbose)
                {
                    _EchoDebug(debugName + "File " + list[favoriteInt].FileName + " with the size " + list[favoriteInt].Size
                        + " is less than or equal to "
                        + target + " and meets conditions. Returning 0");
                }
                return favoriteInt;
            }


            if (list[favoriteInt].Size > target)
            {
                if (verbose)
                {
                    _EchoDebug(debugName + "Element 0: " + list[0].FileName + " with the size " + list[0].Size + " is larger than the target value of "
                        + target);
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Size == target && meetsConditions(list[i], maxSize))
                    {
                        if (verbose)
                        {
                            _EchoDebug(debugName + list[i].FileName + " size " + list[i].Size + " matches the target of " + target);
                        }
                        return i;
                    }
                    if (list[i].Size > target && meetsConditions(list[i],maxSize))
                    {
                        if (verbose)
                        {
                            _EchoDebug(debugName + list[i].FileName + " size " + list[i].Size + " is larger than the target of " + target
                                + " and the favorite is being set to this file.");
                        }
                        favorite = i;

                    }
                    if (list[i].Size < target && meetsConditions(list[i], maxSize))
                    {
                        if (target - list[i].Size == favorite - target)
                        {
                            if (verbose)
                            {
                                _EchoDebug(debugName + list[i].FileName + " size " + list[i].Size + " is less than the target of " + target
                                    + " and has an equal distance from the target as the previous favorite. Returning the previous (larger) favorite.");
                            }
                            return favorite;
                        }
                        if (target - list[i].Size < favorite - target)
                        {
                            if (verbose)
                            {
                                _EchoDebug(debugName + list[i].FileName + " size " + list[i].Size + " is less than the target of " + target
                                    + "and the difference is closer than the difference between the target and previous favorite. Returning this file.");
                            }
                            return i;
                        }
                        if (verbose)
                        {
                            int a = favorite ?? default(int);
                            _EchoDebug(debugName + list[i].FileName + " size " + list[i].Size + " is less than the target of " + target
                                + " but is a greater difference from the target than the previous favorite. Returning the favorite value of: "
                                + favorite + " which is " + list[a] + " " + list[a].Size);
                        }
                        return favorite;
                    }
                }
            }

            if (verbose)
            {
                _EchoDebug(debugName + "The loop exited without returning a value. Returning " + favorite);
            }

            return favorite;


        }


        //Substring for List<FileProps>s! Pass an int as an argument and it will return the contents of the original list
        //beginning from that index
        public List<FileProps>? Sublist(List<FileProps>list, int startFrom)
        {
            if (list.Count == 0)
            {
                _Error("sublist: Empty list passed");
                return null;
            }

            if (startFrom > list.Count)
            {
                _Error("sublist: startFrom out of range");
                return null;
            }

            List<FileProps> theList = new List<FileProps>();

            for (int i = startFrom; i < list.Count; i++) theList.Add(list[i]);

            return theList;
        }

        
                                        //End burn organizing related code



        //Pass a List<FileProps> and it will tell you the total size, in bytes, of its contents
        public long FileListTotalSize(List<FileProps> fileList)
        {
            long sizeTotal = 0;
            for (int i = 0; i < fileList.Count; i++)
            {
                sizeTotal += fileList[i].Size;
            }
            return sizeTotal;
        }

        


        //Sets the lastError text
        private void _Error(string text)
        {
            _lastError = text;
        }

        public string GetLastError()
        {
            return _lastError;
        }

        //Pass a path and get just the filename
        public static string GetFilenameFromPath(string path)
        {
            for (int i = path.Length - 1; i > 0; i--)
            {
                if (path.Substring(i, 1) == "\\")
                {
                    return (path.Substring(i + 1));
                }
            }
            Console.WriteLine("Invalid path or parsing error");
            return "";
        }


        public void PrintFiles()
        {
            Console.WriteLine("Printing all files in " + ThisPool + ":");
            for (int i = 0; i < AllFiles.Count; i++)
            {
                Console.WriteLine(AllFiles[i].Size + " " + AllFiles[i].FileName);
            }
        }

        //Return a string with all the files from this pool
        public string BurnPoolToString()
        {
            string result = "";

            for (int i = 0; i < AllFiles.Count; i++)
            {
                string checksum = "";
                for (int x = 0; x < AllFiles[i].Checksum.Length; x++) checksum += AllFiles[i].Checksum[x];
                result += AllFiles[i].FileName + " " + checksum + "\n";
            }
            return result;
        }

        public bool PrintBurnQueueInfo()
        {
            _EchoDebug("Printing burnQueue info");
            for (int i = 0; i < BurnQueue.Count; i++)
            {
                _EchoDebug(i + " " + BurnQueue[i].Name + " elements: " + BurnQueue[i].Files.Count);
            }

            return true;
        }

        public bool PrintBurnQueueDetailed()
        {
            _EchoDebug("Printing detailed burnQueue info");
            for (int i = 0; i < BurnQueue.Count; i++)
            {
                _EchoDebug(i + " " + BurnQueue[i].Name + " elements: " + BurnQueue[i].Files.Count);
                for (int x = 0; x < BurnQueue[i].Files.Count; x++)
                {
                    _EchoDebug(BurnQueue[i].Files[x].FileName);
                }
            }

            return true;
        }

        private void _EchoDebug(string text)
        {
            //Console.WriteLine(text);
            if (MainWindow != null) MainWindow.DebugEcho(text);
            else
            {
                throw new NullReferenceException("mainWindow in BurnPoolManager is null, debug output failed.");
            }
        }

        private void _EchoDebugA(string text)
        {
            if (MainWindow != null) MainWindow.DebugEchoAsync(text);
            else
            {
                throw new NullReferenceException("mainWindow in BurnPoolManager is null, debug output failed.");
            }
        }

        private void _LogOutput(string text)
        {
            //Console.WriteLine(text);
            if (MainWindow != null) MainWindow.LogOutput(text);
            else
            {
                throw new NullReferenceException("mainWindow in BurnPoolManager is null, debug output failed.");
            }
        }

        public void Test()
        {
            _EchoDebug("WE are haveing a test!!");
        }
        

    }
}
