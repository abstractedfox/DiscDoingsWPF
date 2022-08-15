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

namespace DiscDoingsWPF
{
    //[Serializable()]
    public class BurnPoolManager
    {
        public List<FileProps> allFiles { get; set; }
        public List<FileProps> allFilesNotInBurnQueue { get; set; }
        private string? lastError;
        public string thisPool { get; set; } //The name of this pool
        public List<OneBurn> burnQueue { get; set; } //Burns which are ready
        //public List<OneBurn> completedBurns { get; set; } //Burns which have been completed
        public hashTypes hashTypeUsed { get; set; }
        public MainWindow? mainWindow; //This will be used to get access to the debug string in MainWindow

        public const int formatVersion = 2;
        //formatVersion 1 adds the hashTypes enum, the hashType field to FileProps,
        //the HashTypeUsed field to BurnPoolManager, the createHashMD5 function, and this field indicating the format version.
        //Version 2: Added "timesBurned" field to OneBurn and remoived the "List<OneBurn> completedBurns" field


        public enum hashTypes
        {
            MD5,
            SHA256
        }

        //the get; set; properties do a lot of stuff but in this case they're necessary for serialization

        //Properties of a single file
        public struct FileProps
        {
            public string fileName { get; set; }
            public string originalPath{get; set;}
            public List<string> discsBurned { get; set; } //A list of every disc that this has been burned to
            public long size { get; set; } //Size in bytes
            public byte[] checksum { get; set; } //Checksum of this file
            public string timeAdded { get; set; } //Date and time added to this pool
            public hashTypes hashType { get; set; }

            public string lastModified { get; set; } //Last modified time from file system
            public bool isExtra { get; set; } //Set this flag to true if this file is intentionally meant to be burned to more than one disc
        }

        //Describes the contents that will go into one single burn
        public class OneBurn
        {
            public long sizeOfVolume { get; set; }
            public long spaceRemaining { get; set; }
            public long spaceUsed { get; set; }
            public List<FileProps> files { get; set; } //Files to be burned. Do not alter this directly!
            public string name { get; set; }
            public int timesBurned; //The number of times this OneBurn has been burned to a physical disc
            
            

            public OneBurn(long fileSizeBytes)
            {
                files = new List<FileProps>();
                spaceRemaining = fileSizeBytes;
                sizeOfVolume = fileSizeBytes;
                name = "Untitled OneBurn";
                timesBurned = 0;
            }

            public OneBurn() //Including a parameter-less constructor is necessary to use JsonSerializer
            {
                files = new List<FileProps>();
                spaceRemaining = 0;
                sizeOfVolume = 0;
                name = "Untitled OneBurn";
                timesBurned = 0;
            }

            //Calculates the value for spaceUsed and spaceRemaining
            public void findSpaceRemaining()
            {
                spaceRemaining = 0;
                spaceUsed = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    spaceUsed += files[i].size;
                }
                spaceRemaining = sizeOfVolume - spaceUsed;
            }



            //Returns false if the file is too big for the current instance

            public bool addFile(FileProps file)
            {
                if (canAddFile(file)) {
                    files.Add(file);
                    findSpaceRemaining();
                    sortFilesBySize();
                    return true;
                }
                return false;
            }

            //Pass an index in (files) and it will remove that file and adjust other values properly
            public bool removeFile(int index)
            {
                if (!files.Remove(files[index]))
                {
                    return false;
                }

                findSpaceRemaining();
                return true;
            }

            //Determines whether there's enough space in this instance to add another file
            public bool canAddFile(FileProps file)
            {
                if (file.size > spaceRemaining) return false;
                return true;
            }

            public void sortFilesBySize()
            {
                FileProps temphold;

                if (files.Count < 2) return;

                for (int i = 0; i < files.Count - 1; i++)
                {
                    //Console.WriteLine(i + " About to compare " + allFiles[i].fileName + " " + allFiles[i].size + " to " +
                    //allFiles[i + 1].fileName + " " + allFiles[i + 1].size);
                    if (files[i + 1].size > files[i].size)
                    {
                        temphold = files[i];
                        files[i] = files[i + 1];
                        files[i + 1] = temphold;
                        i = -1;
                    }
                }
            }

            public bool setName(string newName)
            {
                name = newName;

                return true;
            }

            //Pass the full path of a file. If that file exists in this OneBurn, it returns the index. If not, it returns -1
            public int findFileByFullPath(string path)
            {
                const bool msgBox = false;
                const string debugName = "BurnPoolManager::OneBurn::findFileByFullPath():";
                for (int i = 0; i < files.Count; i++)
                {
                    if (msgBox)
                    {
                        System.Windows.MessageBox.Show(debugName + "Comparing " + files[i].originalPath + " with " + path);
                    }
                    if (files[i].originalPath == path) return i;
                    if (msgBox) System.Windows.MessageBox.Show(debugName + "Did not match");
                }
                if (msgBox) System.Windows.MessageBox.Show(debugName + "No matches found");
                return -1;
            }

            public void print()
            {
                Console.WriteLine("Printing contents of " + name);
                for (int i = 0; i < files.Count; i++)
                {
                    Console.WriteLine(files[i].size + " " + files[i].fileName);
                }
            }

        }

        public BurnPoolManager()
        {
            allFiles = new List<FileProps>();
            allFilesNotInBurnQueue = new List<FileProps>();
            burnQueue = new List<OneBurn>();
            //completedBurns = new List<OneBurn>();
            thisPool = "Untitled Pool";
            lastError = "";
            hashTypeUsed = hashTypes.SHA256;
            mainWindow = null;
        }

        public BurnPoolManager(MainWindow programMainWindow)
        {
            allFiles = new List<FileProps>();
            allFilesNotInBurnQueue = new List<FileProps>();
            burnQueue = new List<OneBurn>();
            //completedBurns = new List<OneBurn>();
            thisPool = "Untitled Pool";
            lastError = "";
            hashTypeUsed = hashTypes.SHA256;
            mainWindow = programMainWindow;
        }


        //Copy constructor
        
        public BurnPoolManager(BurnPoolManager copySource)
        {
            const string debugName = "BurnPoolManager(BurnPoolManager copySource) (copy function):";
            //this.allFiles = copySource.allFiles;

            if (copySource.allFiles == null) this.allFiles = new List<FileProps>();
            else
            {
                this.allFiles = new List<FileProps>();
                for (int i = 0; i < copySource.allFiles.Count; i++)
                {
                    this.allFiles.Add(copySource.allFiles[i]);
                }
            }

            if (copySource.allFilesNotInBurnQueue == null) this.allFilesNotInBurnQueue = new List<FileProps>();
            else
            {
                this.allFilesNotInBurnQueue = new List<FileProps>();
                for (int i = 0; i < copySource.allFilesNotInBurnQueue.Count; i++)
                {
                    this.allFilesNotInBurnQueue.Add(copySource.allFilesNotInBurnQueue[i]);
                }
            }

            //this.burnQueue = copySource.burnQueue;
            if (copySource.burnQueue == null) this.burnQueue = new List<OneBurn>();
            else
            {
                this.burnQueue = new List<OneBurn>();
                for (int i = 0; i < copySource.burnQueue.Count; i++)
                {
                    this.burnQueue.Add(copySource.burnQueue[i]);
                }
            }

            //this.completedBurns = copySource.completedBurns;
            /*
            if (copySource.completedBurns == null) this.completedBurns = new List<OneBurn>();
            else
            {
                this.completedBurns = new List<OneBurn>();
                for (int i = 0; i < copySource.completedBurns.Count; i++)
                {
                    this.completedBurns.Add(copySource.completedBurns[i]);
                }
            }
            */

            if (copySource.hashTypeUsed == null)
            {
                //If there is no hashTypeUsed then the type is most likely SHA256, since that was what was used before MD5 was made default
                this.hashTypeUsed = hashTypes.SHA256;
                echoDebug(debugName + "No hash type was found in the source file. Assuming SHA256.");
            }
            else
            {
                this.hashTypeUsed = copySource.hashTypeUsed;
            }

            this.thisPool = copySource.thisPool;
            this.lastError = copySource.lastError;
            this.mainWindow = copySource.mainWindow;
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
        public Task addFileAsync(StorageFile storageFileIn)
        {
            const bool debugVerbose = true;

            const string validPath = @"(^\\{2}.+\\(.)+[^\\])|(([a-z]|[A-Z])+:\\.+[^\\])",
                debugName = "addFileAsync:";
            Regex compare = new Regex(validPath);

            if (!storageFileIn.IsAvailable)
            {
                echoDebug(debugName + " error: storageFileIn.IsAvailable = false for this file: " + storageFileIn.Path.ToString() + "\n");
                throw new Exception(debugName + "storageFileIn.IsAvailable = false for this file: " + storageFileIn.Path.ToString());
                return null;
            }
            if (storageFileIn == null)
            {
                echoDebug(debugName + " error: storageFileIn == null");
                throw new NullReferenceException(debugName + " error: storageFileIn == null");
                return null;
            }

            if (!compare.IsMatch(storageFileIn.Path.ToString()))
            {
                echoDebug("addFile(StorageFile) error: File does not appear to be a valid system or network path with " +
                    "path and extension: " + storageFileIn.Path.ToString() + "\n");
                throw new Exception(debugName + "error: File does not appear to be a valid system or network path with " +
                    "path and extension: " + storageFileIn.Path.ToString());
                return null;
            }


            FileInfo fInfo = new FileInfo(storageFileIn.Path);


            string path = fInfo.FullName;

            return Task.Run(() => {
                FileProps newFile = new FileProps();
                newFile.fileName = getFilenameFromPath(path);
                newFile.originalPath = path;
                newFile.size = fInfo.Length;
                newFile.timeAdded = DateTime.Now.ToString();
                newFile.lastModified = fInfo.LastWriteTime.ToString();
                newFile.isExtra = false;
                newFile.discsBurned = new List<String>();

                string hashString = "";
                byte[] hashtime = createHash(fInfo);
                for (int i = 0; i < hashtime.Length; i++)
                {
                    hashString += hashtime[i].ToString();
                }
                newFile.checksum = hashtime;

                allFiles.Add(newFile);
                allFilesNotInBurnQueue.Add(newFile);
                //mainWindow.filesInChecksumQueue--;
            });

        }


        //Don't make this async, it will probably cause problems
        //Returns false if there is an issue
        public bool removeFile(string fullPath)
        {
            const string debugName = "BurnPoolManager::removeFile():";
            //Need to remove it from the main list and check the burn queue for that file
            FileProps fileToRemove = allFiles[findFileByFullPath(fullPath)];

            if (fileToRemove.discsBurned.Count > 0)
            {
                echoDebug(debugName + "File \"" + fileToRemove.originalPath + "\" has been marked as burned. To remove it, first " +
                    "delete all discs that it has been burned to.");
                return false;
            }

            if (!allFiles.Remove(fileToRemove))
            {
                echoDebug(debugName + "File " + fullPath + " was not found in this BurnPoolManager.");
                return false;
            }
            
            allFilesNotInBurnQueue.Remove(fileToRemove).ToString();

            //Remove it from all OneBurns with that file
            for (int i = 0; i < burnQueue.Count; i++)
            {
                for (int x = 0; x < burnQueue[i].files.Count; x++)
                {
                    if (FilePropsEqual(burnQueue[i].files[x], fileToRemove))
                    {
                        burnQueue[i].files.RemoveAt(x);
                        x -= 1; 
                    }
                }
            }
            return true;
        }

        //Use this any time a file is removed from a OneBurn. This is necessary in order to update allFilesNotInBurnQueue
        //This will place the file back in allFilesNotInBurnQueue at the end, so run sortFilesBySize() after calling it
        public bool removeFileFromOneBurn(int oneBurnIndex, int fileIndex)
        {
            const string debugName = "BurnPoolManager::removeFileFromOneBurn():";
            const bool debug = false;

            if (debug)
            {
                echoDebug(debugName + "Starting with burnQueue: " + oneBurnIndex + " fileIndex: " + fileIndex);
                //System.Windows.MessageBox.Show(debugName + "Starting with burnQueue: " + oneBurnIndex + " fileIndex: " + fileIndex);
            }

            FileProps fileToRemove = burnQueue[oneBurnIndex].files[fileIndex];

            if (oneBurnIndex > burnQueue.Count || oneBurnIndex < 0)
            {
                echoDebug(debugName + "OneBurn index is out of range.");
                return false;
            }
            if (burnQueue[oneBurnIndex].files.Count < fileIndex || fileIndex < 0)
            {
                echoDebug(debugName + "File index is out of range.");
                return false;
            }

            if (burnQueue[oneBurnIndex].timesBurned > 0)
            {
                try
                {
                    mainWindow.informUser(MainWindow.userMessages.DISC_ALREADY_BURNED);
                    return false;
                }
                catch(NullReferenceException)
                {
                    echoDebug(debugName + "MainWindow reference is null.");
                }
            }

            try
            {
                if (!burnQueue[oneBurnIndex].removeFile(fileIndex))
                {
                    echoDebug(debugName + "Attempt to remove file[" + fileIndex + "] in OneBurn[" + oneBurnIndex + "] " +
                        "was unsuccessful.");
                    return false;
                }
                allFilesNotInBurnQueue.Add(fileToRemove);
                allFiles[findFileByFullPath(fileToRemove.originalPath)].discsBurned.Remove(burnQueue[oneBurnIndex].name);
                return true;

            }
            catch (NullReferenceException)
            {
                echoDebug(debugName + "Attempt to remove file[" + fileIndex + "] in OneBurn[" + oneBurnIndex + "] " +
                        "resulted in a null reference and was unsuccessful.");
                return false;
            }

            echoDebug(debugName + "Exited without successfully removing file or throwing a known error.");
            return false;
        }

        //Deletes a OneBurn from the list, restoring all files which are not in other OneBurns to the allFilesNotInBurnQueue
        //Make sure to check that said files aren't in other OneBurns, since that's possible to do.
        //This function will -not- check the timesBurned field to see if the OneBurn has been burned to a disc.
        //The calling function should make any compensations for that on its own!
        public bool deleteOneBurn(int oneBurnIndex)
        {
            const string debugName = "BurnPoolManager::deleteOneBurn:";
            const bool debug = false;


            if (oneBurnIndex > burnQueue.Count || oneBurnIndex < 0)
            {
                echoDebug(debugName + "Invalid OneBurn index.");
                return false;
            }


            FileProps temphold = new FileProps();
            for (int i = 0; i < burnQueue[oneBurnIndex].files.Count; i++)
            {
                temphold = burnQueue[oneBurnIndex].files[i];
                if (!burnQueue[oneBurnIndex].removeFile(i))
                {
                    echoDebug(debugName + "Removal of file index " + i + " of burnQueue index " + oneBurnIndex + " failed." +
                        " Halting removal of OneBurn.");
                    return false;
                }
                if (!existsInBurnQueue(temphold))
                {
                    allFilesNotInBurnQueue.Add(temphold);
                }

                int indexInMainList = findFileByFullPath(temphold.originalPath);
                for (int x = 0; x < allFiles[indexInMainList].discsBurned.Count; x++)
                {
                    if (allFiles[indexInMainList].discsBurned[x] == burnQueue[oneBurnIndex].name)
                    {
                        allFiles[indexInMainList].discsBurned.RemoveAt(x);
                    }
                }
            }

            sortFilesBySize();

            burnQueue.RemoveAt(oneBurnIndex);
            return true;
        }

        public byte[] createHash(FileInfo fInfo)
        {
            const string debugName = "createHash:";
            if (hashTypeUsed == hashTypes.SHA256) return (createHashSHA256(fInfo));
            if (hashTypeUsed == hashTypes.MD5) return (createHashMD5(fInfo));

            echoDebug(debugName + "Checksum error: hashTypeUsed appears to be invalid: " + hashTypeUsed);
            return new byte[0];
        }


        public byte[] createHashSHA256(FileInfo fInfo)
        {
            const string debugName = "createHashSHA256:";
            //do the hashing
            using (SHA256 hashtime = SHA256.Create())
            {
                try
                {
                    using (FileStream dataToHash = fInfo.Open(FileMode.Open))
                    {
                        byte[] hashValue = hashtime.ComputeHash(dataToHash);
                        return hashValue;

                    }
                }
                catch (IOException exception)
                {
                    echoDebug(debugName + "Exception thrown:" + exception);

                }
                catch (UnauthorizedAccessException exception)
                {
                    echoDebug(debugName + "Exception thrown:" + exception);
                }
            }

            echoDebug(debugName + "Checksum error: Exited using block without returning a value.");
            return new byte[0];

        }

        public byte[] createHashMD5(FileInfo fInfo)
        {
            const string debugName = "createHashMD5:";
            //do the hashing
            using (MD5 hashtime = MD5.Create())
            {
                try
                {
                    using (FileStream dataToHash = fInfo.Open(FileMode.Open))
                    {
                        byte[] hashValue = hashtime.ComputeHash(dataToHash);
                        return hashValue;

                    }
                }
                catch (IOException exception)
                {
                    echoDebug(debugName + "Exception thrown:" + exception);

                }
                catch (UnauthorizedAccessException exception)
                {
                    echoDebug(debugName + "Exception thrown:" + exception);
                }
            }

            echoDebug(debugName + "Checksum error: Exited using block without returning a value.");
            return new byte[0];

        }


        //Compare two byte arrays. For comparing checksums
        public static bool byteArrayEqual(byte[] arr1, byte[] arr2)
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
            if (a.fileName != b.fileName) return false;
            if (a.originalPath != b.originalPath) return false;
            if (a.lastModified != b.lastModified) return false;
            if (a.size != b.size) return false;
            if (a.timeAdded != b.timeAdded) return false;
            if (!byteArrayEqual(a.checksum, b.checksum)) return false;
            return true;
        }



                                            //Begin burn organizing related code

        //Checks if a file is already in the burn queue list. Returns false if either the file being added or a found match have the
        //isExtra flag set to true.
        private bool existsInBurnQueue(FileProps file)
        {
            bool debug = false;
            if (debug) echoDebug("existsInBurnQueue start: " + file.fileName);
            if (file.isExtra == true)
            {
                if (debug) echoDebug("existsInBurnQueue: isExtra = true, returning false");
                return false;
            }
            for (int i = 0; i < burnQueue.Count; i++)
            {
                for (int x = 0; x < burnQueue[i].files.Count; x++)
                {
                    if (FilePropsEqual(burnQueue[i].files[x], file) &&
                        burnQueue[i].files[x].isExtra == false){
                        if (debug) echoDebug("existsInBurnQueue: File found in burn queue, returning true");
                        return true;
                        }
                }
            }
            if (debug) echoDebug("existsInBurnQueue: File not found in burn queue, returning false");
            return false;
        }

        //Returns a list containing every file that is in the burnQueue instantiation
        private List<FileProps> allFilesInBurnQueue()
        {
            List<FileProps> combinedLists = new List<FileProps>();
            for (int i = 0; i < burnQueue.Count; i++)
            {
                for (int x = 0; x < burnQueue[i].files.Count; x++)
                {
                    combinedLists.Add(burnQueue[i].files[x]);
                }
            }

            return combinedLists;
        }

        //this one!
        //nevermind not this one

        //Generate a OneBurn and add it to the queue. Returns false if a OneBurn can't be generated.
        public bool generateOneBurn(long sizeInBytes)
        {
            const bool verbose = false;
            const string debugName = "generateOneBurn:";
            OneBurn aBurn = new OneBurn(sizeInBytes);
            aBurn.setName(thisPool + (burnQueue.Count));

            if (verbose) echoDebug(debugName + "Start");

            sortFilesBySize();

            if (verbose) echoDebug(debugName + "Files sorted by size");

            /*
             * Old version of this that needs to be updated to work properly with allFilesNotInBurnQueue
            for (int i = 0; i < allFiles.Count; i++) //Start with the biggest file that will fit
            {
                if (aBurn.canAddFile(allFiles[i]) && !existsInBurnQueue(allFiles[i]))
                {
                    aBurn.addFile(allFiles[i]);

                    break;
                }
            }
            */

            for (int i = 0; i < allFilesNotInBurnQueue.Count; i++) //Start with the biggest file that will fit
            {
                if (aBurn.canAddFile(allFilesNotInBurnQueue[i]))
                {
                    aBurn.addFile(allFilesNotInBurnQueue[i]);
                    allFilesNotInBurnQueue.RemoveAt(i);
                    break;
                }
            }

            long spaceTarget = aBurn.spaceRemaining / 2;
            int passes = 0; //for debug purposes
            if (verbose) echoDebug(debugName + "First file added, loopy next");

            do
            {
                int? nextFile = findFileByNearestSizeA(allFilesNotInBurnQueue, spaceTarget, aBurn.spaceRemaining);

                if (verbose)
                {
                    echoDebug(debugName + " pass " + passes + " nextFile:" + nextFile + " spaceTarget = " + spaceTarget);
                }

                if (nextFile == null) break; //No suitable files were found
                int nextFileInt = nextFile ?? default(int); //necessary to resolve the nullable int

                if (verbose)
                {
                    echoDebug(debugName + "Adding: " + allFilesNotInBurnQueue[nextFileInt]);
                    passes++;
                    mainWindow.debugEchoAsync(passes.ToString());
                    if (passes > 5000) MessageBox.Show("Hey");
                }
                aBurn.addFile(allFilesNotInBurnQueue[nextFileInt]);
                allFilesNotInBurnQueue.RemoveAt(nextFileInt);
                //Note: findFileByNearestSize examines every file in the given List<FileProps> on each pass, and ignores any results that would
                //not fit the limit or that are already in the burn list. For that reason, no additional checks should be needed here as it
                //will return null if it can't find anything that fits these criteria

                spaceTarget = aBurn.spaceRemaining / 2;
            }
            while (true || allFilesNotInBurnQueue.Count > 0);
            if (aBurn.files.Count > 0)
            {
                burnQueue.Add(aBurn);
                if (verbose) echoDebug(debugName + "aBurn generated, exiting " + debugName);
                return true;
            }
            else
            {
                if (verbose) echoDebug(debugName + "aBurn was not generated, exiting " + debugName);
                return false;
            }
            
        }


        //Search the burnQueue list for a OneBurn with the passed filename. If it finds a result, it returns its position in the list.
        //If there is no result, it returns null.
        public int? getBurnQueueFileByName(string oneBurnName)
        {
            const bool debug = false;
            const string debugName = "BurnPoolManager::getBurnQueueFileByName:";
            if (debug)
            {
                echoDebug(debugName + "Starting with oneBurnName: " + oneBurnName);
                echoDebug("Looking in these OneBurns:");
                for (int i = 0; i < burnQueue.Count; i++)
                {
                    echoDebug(burnQueue[i].name);
                }
            }
            for (int i = 0; i < burnQueue.Count; i++)
            {
                if (burnQueue[i].name == oneBurnName)
                {
                    if (debug)
                    {
                        echoDebug(debugName + "OneBurn \"" + burnQueue[i].name + "\" equals \"" + oneBurnName + "\" Returning " + i);
                    }
                    return i;
                }
            }

            if (debug)
            {
                echoDebug(debugName + "No equal results were found, returning null.");
            }
            return null;
        }

        //Sort the files in allFiles by size, with [0] being the largest.
        public void sortFilesBySize()
        {
            const bool debug = false;
            const string debugName = "sortFilesBySize:";
            FileProps temphold;
            int iterations = 0, restarts = 0;

            sortAllFilesNotInBurnQueueBySize();

            if (debug) echoDebug(debugName + "Start");

            if (allFiles.Count < 2) return;

            for(int i = 0; i < allFiles.Count - 1; i++)
            {
                if (debug)
                {
                    iterations++;
                    //if (iterations % 5000 == 0) echoDebug(iterations.ToString() + " " + restarts.ToString());
                }
                //Console.WriteLine(i + " About to compare " + allFiles[i].fileName + " " + allFiles[i].size + " to " +
                    //allFiles[i + 1].fileName + " " + allFiles[i + 1].size);
                if(allFiles[i + 1].size > allFiles[i].size)
                {
                    if (debug)
                    {
                        if (restarts % 5000 == 0) echoDebug(restarts + ": " + allFiles[i + 1].size 
                            + " is larger than " + allFiles[i].size);
                    }
                    temphold = allFiles[i];
                    allFiles[i] = allFiles[i + 1];
                    allFiles[i + 1] = temphold;
                    
                    //Keep moving it up the list as long as it's bigger than the one before it
                    while (i > 0 && allFiles[i].size > allFiles[i - 1].size)
                    {
                        temphold = allFiles[i - 1];
                        allFiles[i - 1] = allFiles[i];
                        allFiles[i] = temphold;
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
        public void sortAllFilesNotInBurnQueueBySize()
        {
            const bool debug = false;
            const string debugName = "BurnPoolManager::sortFilesBySize(ref List<FileProps> listToSort):";
            FileProps temphold;
            int iterations = 0, restarts = 0;

            if (debug) echoDebug(debugName + "Start");

            if (allFilesNotInBurnQueue.Count < 2) return;

            for (int i = 0; i < allFilesNotInBurnQueue.Count - 1; i++)
            {
                if (debug)
                {
                    iterations++;
                    //if (iterations % 5000 == 0) echoDebug(iterations.ToString() + " " + restarts.ToString());
                }
                //Console.WriteLine(i + " About to compare " + allFiles[i].fileName + " " + allFiles[i].size + " to " +
                //allFiles[i + 1].fileName + " " + allFiles[i + 1].size);
                if (allFilesNotInBurnQueue[i + 1].size > allFilesNotInBurnQueue[i].size)
                {
                    if (debug)
                    {
                        if (restarts % 5000 == 0) echoDebug(restarts + ": " + allFiles[i + 1].size
                            + " is larger than " + allFiles[i].size);
                    }
                    temphold = allFilesNotInBurnQueue[i];
                    allFilesNotInBurnQueue[i] = allFilesNotInBurnQueue[i + 1];
                    allFilesNotInBurnQueue[i + 1] = temphold;

                    //Keep moving it up the list as long as it's bigger than the one before it
                    while (i > 0 && allFilesNotInBurnQueue[i].size > allFilesNotInBurnQueue[i - 1].size)
                    {
                        temphold = allFilesNotInBurnQueue[i - 1];
                        allFilesNotInBurnQueue[i - 1] = allFilesNotInBurnQueue[i];
                        allFilesNotInBurnQueue[i] = temphold;
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
        public bool commitOneBurn(int burnQueueMember)
        {
            const string debugName = "BurnPoolManager::commitOneBurn:";
            const bool debug = false;

            if (burnQueueMember > burnQueue.Count || burnQueueMember < 0){
                echoDebug(debugName + "Index of " + burnQueueMember + " is out of range.");
                return false;
            }
            if (burnQueue[burnQueueMember] == null)
            {
                echoDebug(debugName + "burnQueue index " + burnQueueMember + " is null.");
                return false;
            }

            for (int i = 0; i < burnQueue[burnQueueMember].files.Count; i++)
            {
                allFiles[findFileByFullPath(burnQueue[burnQueueMember].files[i].originalPath)].discsBurned.Add(burnQueue[burnQueueMember].name);
            }

            burnQueue[burnQueueMember].timesBurned++;
            return true;
        }

        //If this member in the burnQueue has been marked as burned, unmark it and remove that OneBurn from the 
        //discsBurned field of all FileProps in allFiles
        public bool uncommitOneBurn(int burnQueueMember)
        {
            const string debugName = "BurnPoolManager::uncommitOneBurn():";
            const bool debug = false;

            if (burnQueueMember > burnQueue.Count || burnQueueMember < 0)
            {
                echoDebug(debugName + "Index of " + burnQueueMember + " is out of range.");
                return false;
            }
            if (burnQueue[burnQueueMember] == null)
            {
                echoDebug(debugName + "burnQueue index " + burnQueueMember + " is null.");
                return false;
            }

            if (burnQueue[burnQueueMember].timesBurned == 0)
            {
                echoDebug("burnQueue[" + burnQueueMember + "] has not been committed to a disc.");
                return false;
            }

            for (int i = 0; i < burnQueue[burnQueueMember].files.Count; i++)
            {
                int locationInAllFiles = findFileByFullPath(burnQueue[burnQueueMember].files[i].originalPath);

                if (locationInAllFiles == -1)
                {
                    echoDebug(debugName + "The file \"" + burnQueue[burnQueueMember].files[i].originalPath + "\" was not found in " +
                        "allFiles. This may indicate corruption or that data was mishandled during file deletion or creation of " +
                        "this OneBurn.");
                    return false;
                }

                int thisBurnInFile = allFiles[locationInAllFiles].discsBurned.IndexOf(burnQueue[burnQueueMember].name);
                if (thisBurnInFile > -1)
                {
                    allFiles[locationInAllFiles].discsBurned.RemoveAt(thisBurnInFile);
                }
                else
                {
                    echoDebug(debugName + "The OneBurn titled \"" + burnQueue[burnQueueMember].name + "\" at burnQueue index " +
                        burnQueueMember + " did not appear in the file \"" + allFiles[locationInAllFiles].originalPath +
                        "\". This may indicate corruption in the struct or an error in how this OneBurn was marked burned.");
                    return false;
                }
            }

            burnQueue[burnQueueMember].timesBurned--;
            return true;
        }


        //Pass a List<FileProps> and a size constraint and receive a new List<FileProps> that has been filled in with files from the
        //source list, beginning at the largest, that will fit within that limit. 'exclude' is files found in source that will not be
        //considered
        public List<FileProps>? generateListByFileSize(long sizeLimit, List<FileProps> source, List<FileProps> exclude)
        {
            bool debug = true;
            string debugName = "generateListByFileSize:";

            long totalSize = 0, runningTotalSize = 0;
            List<FileProps> complete = new List<FileProps>();
            if (sizeLimit == 0)
            {
                error("generateListByFileSize error: Size limit set to 0");
                return null;
            }
            if (source.Count == 0)
            {
                error("generateListByFileSize error: No files in List<FileProps> source");
                return null;
            }

            for (int i = 0; i < source.Count; i++)
            {
                totalSize += source[i].size;
            }

            if (totalSize < sizeLimit)
            {
                error("generateListByFileSize: Source list is smaller than file size limit, returning source list");
                return source;
            }

            for (int i = 0; i < source.Count; i++)
            {
                if (debug)
                {
                    echoDebug(debugName + "Running total: " + runningTotalSize + " Size limit: " + sizeLimit + " File: " 
                        + source[i].fileName + " File size: " + source[i].size);
                    echoDebug(debugName + "Filesize + running total: " + source[i].size + runningTotalSize);
                }

                if (source[i].size + runningTotalSize < sizeLimit)
                {
                    if (findFile(source[i], exclude) == -1)
                    {
                        if (debug)
                        {
                            echoDebug(debugName + "The file " + source[i].fileName + " " + source[i].size + " is not in the burn list already," +
                                "so it is added.");
                        }

                        complete.Add(source[i]);
                        runningTotalSize += source[i].size;
                    }
                }
            }

            return complete;
        }


        //Pass a FileProps and a list, if there is an identical FileProps in that list, it will return its position.
        //Returns -1 if there is no match
        public int findFile(FileProps file, List<FileProps> fileList)
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
        public int findFileByFullPath(string path)
        {
            for (int i = 0; i < allFiles.Count; i++)
            {
                if (allFiles[i].originalPath == path) return i;
            }
            return -1;
        }
    
        //Pass a List<FileProps> and a desired file size, it will return the position in 'list' of the element whose file size is nearest
        //(higher or lower) to that size. If there is an equal difference between the next highest and lowest file, it will return the highest.
        //Expects the List<FileProps> to be ordered from largest-smallest (0 is largest)
        //Any files identical to files in 'ignore' or larger than 'maxSize' will not be considered. Returns null if no suitable files are found
        public int? findFileByNearestSize(List<FileProps> list, long target, List<FileProps> ignore, long maxSize)
        {
            bool verbose = false;
            string debugName = "findFileByNearestSize:";
            int? favorite = null;
            Func<FileProps, List<FileProps>, long, bool> meetsConditions = (aFile, ignoreList, sizeLimit) =>
                (findFile(aFile, ignoreList) == -1) && aFile.size <= maxSize && (!existsInBurnQueue(aFile));

            if (verbose)
            {
                echoDebug(debugName + "Starting with target " + target + " and maxSize " + maxSize);
            }

            for (int i = 0; i < list.Count; i++) //Start with the favorite set to the biggest file that meets criteria
            {
                if (meetsConditions(list[i], ignore, maxSize)) favorite = i;
            }

            if (favorite == null) return favorite;
            int favoriteInt = favorite ?? default(int);

            
            if (list[favoriteInt].size <= target && meetsConditions(list[favoriteInt], ignore, maxSize))
            {
                if (verbose)
                {
                    echoDebug(debugName + "File " + list[favoriteInt].fileName + " with the size " + list[favoriteInt].size 
                        + " is less than or equal to "
                        + target + " and meets conditions. Returning 0");
                }
                return favoriteInt;
            }
            

            if (list[favoriteInt].size > target)
            {
                if (verbose)
                {
                    echoDebug(debugName + "Element 0: " + list[0].fileName + " with the size " + list[0].size + " is larger than the target value of " 
                        + target);
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].size == target && meetsConditions(list[i], ignore, maxSize))
                    {
                        if (verbose)
                        {
                            echoDebug(debugName + list[i].fileName + " size " + list[i].size + " matches the target of " + target);
                        }
                        return i;
                    }
                    if (list[i].size > target && meetsConditions(list[i], ignore, maxSize))
                    {
                        if (verbose)
                        {
                            echoDebug(debugName + list[i].fileName + " size " + list[i].size + " is larger than the target of " + target 
                                + " and the favorite is being set to this file.");
                        }
                        favorite = i;

                    }
                    if (list[i].size < target && meetsConditions(list[i], ignore, maxSize))
                    {
                        if (target - list[i].size == favorite - target)
                        {
                            if (verbose)
                            {
                                echoDebug(debugName + list[i].fileName + " size " + list[i].size + " is less than the target of " + target
                                    + " and has an equal distance from the target as the previous favorite. Returning the previous (larger) favorite.");
                            }
                            return favorite;
                        }
                        if (target - list[i].size < favorite - target)
                        {
                            if (verbose)
                            {
                                echoDebug(debugName + list[i].fileName + " size " + list[i].size + " is less than the target of " + target
                                    + "and the difference is closer than the difference between the target and previous favorite. Returning this file.");
                            }
                            return i;
                        }
                        if (verbose)
                        {
                            int a = favorite ?? default(int);
                            echoDebug(debugName + list[i].fileName + " size " + list[i].size + " is less than the target of " + target
                                + " but is a greater difference from the target than the previous favorite. Returning the favorite value of: " 
                                + favorite + " which is " + list[a] + " " + list[a].size);
                        }
                        return favorite;
                    }
                }
            }

            if (verbose)
            {
                echoDebug(debugName + "The loop exited without returning a value. Returning " + favorite);
            }

            return favorite;


        }

        //Modify to no longer utilize the ignore list or check if a file is in the burn queue, only searching the List<FileProps> list
        //for the nearest file
        public int? findFileByNearestSizeA(List<FileProps> list, long target, long maxSize)
        {
            bool verbose = false;
            string debugName = "findFileByNearestSizeA:";
            int? favorite = null;
            Func<FileProps, long, bool> meetsConditions = (aFile, sizeLimit) => (aFile.size <= maxSize);

            if (verbose)
            {
                echoDebug(debugName + "Starting with target " + target + " and maxSize " + maxSize);
            }

            for (int i = 0; i < list.Count; i++) //Set the favorite to the biggest file that meets criteria
            {
                if (meetsConditions(list[i], maxSize)) favorite = i;
            }

            if (favorite == null) return favorite;
            int favoriteInt = favorite ?? default(int);


            if (list[favoriteInt].size <= target && meetsConditions(list[favoriteInt], maxSize))
            {
                if (verbose)
                {
                    echoDebug(debugName + "File " + list[favoriteInt].fileName + " with the size " + list[favoriteInt].size
                        + " is less than or equal to "
                        + target + " and meets conditions. Returning 0");
                }
                return favoriteInt;
            }


            if (list[favoriteInt].size > target)
            {
                if (verbose)
                {
                    echoDebug(debugName + "Element 0: " + list[0].fileName + " with the size " + list[0].size + " is larger than the target value of "
                        + target);
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].size == target && meetsConditions(list[i], maxSize))
                    {
                        if (verbose)
                        {
                            echoDebug(debugName + list[i].fileName + " size " + list[i].size + " matches the target of " + target);
                        }
                        return i;
                    }
                    if (list[i].size > target && meetsConditions(list[i],maxSize))
                    {
                        if (verbose)
                        {
                            echoDebug(debugName + list[i].fileName + " size " + list[i].size + " is larger than the target of " + target
                                + " and the favorite is being set to this file.");
                        }
                        favorite = i;

                    }
                    if (list[i].size < target && meetsConditions(list[i], maxSize))
                    {
                        if (target - list[i].size == favorite - target)
                        {
                            if (verbose)
                            {
                                echoDebug(debugName + list[i].fileName + " size " + list[i].size + " is less than the target of " + target
                                    + " and has an equal distance from the target as the previous favorite. Returning the previous (larger) favorite.");
                            }
                            return favorite;
                        }
                        if (target - list[i].size < favorite - target)
                        {
                            if (verbose)
                            {
                                echoDebug(debugName + list[i].fileName + " size " + list[i].size + " is less than the target of " + target
                                    + "and the difference is closer than the difference between the target and previous favorite. Returning this file.");
                            }
                            return i;
                        }
                        if (verbose)
                        {
                            int a = favorite ?? default(int);
                            echoDebug(debugName + list[i].fileName + " size " + list[i].size + " is less than the target of " + target
                                + " but is a greater difference from the target than the previous favorite. Returning the favorite value of: "
                                + favorite + " which is " + list[a] + " " + list[a].size);
                        }
                        return favorite;
                    }
                }
            }

            if (verbose)
            {
                echoDebug(debugName + "The loop exited without returning a value. Returning " + favorite);
            }

            return favorite;


        }


        //Substring for List<FileProps>s! Pass an int as an argument and it will return the contents of the original list
        //beginning from that index
        public List<FileProps>? sublist(List<FileProps>list, int startFrom)
        {
            if (list.Count == 0)
            {
                error("sublist: Empty list passed");
                return null;
            }

            if (startFrom > list.Count)
            {
                error("sublist: startFrom out of range");
                return null;
            }

            List<FileProps> theList = new List<FileProps>();

            for (int i = startFrom; i < list.Count; i++) theList.Add(list[i]);

            return theList;
        }

        
                                        //End burn organizing related code


        //Pass a List<FileProps> and it will tell you the total size, in bytes, of its contents
        public long fileListTotalSize(List<FileProps> fileList)
        {
            long sizeTotal = 0;
            for (int i = 0; i < fileList.Count; i++)
            {
                sizeTotal += fileList[i].size;
            }
            return sizeTotal;
        }

        


        //Sets the lastError text
        private void error(string text)
        {
            lastError = text;
        }

        public string getLastError()
        {
            return lastError;
        }

        //Pass a path and get just the filename
        public static string getFilenameFromPath(string path)
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


        public void printFiles()
        {
            Console.WriteLine("Printing all files in " + thisPool + ":");
            for (int i = 0; i < allFiles.Count; i++)
            {
                Console.WriteLine(allFiles[i].size + " " + allFiles[i].fileName);
            }
        }

        //Return a string with all the files from this pool
        public string burnPoolToString()
        {
            string result = "";

            for (int i = 0; i < allFiles.Count; i++)
            {
                string checksum = "";
                for (int x = 0; x < allFiles[i].checksum.Length; x++) checksum += allFiles[i].checksum[x];
                result += allFiles[i].fileName + " " + checksum + "\n";
            }
            return result;
        }

        public bool printBurnQueueInfo()
        {
            echoDebug("Printing burnQueue info");
            for (int i = 0; i < burnQueue.Count; i++)
            {
                echoDebug(i + " " + burnQueue[i].name + " elements: " + burnQueue[i].files.Count);
            }

            return true;
        }

        public bool printBurnQueueDetailed()
        {
            echoDebug("Printing detailed burnQueue info");
            for (int i = 0; i < burnQueue.Count; i++)
            {
                echoDebug(i + " " + burnQueue[i].name + " elements: " + burnQueue[i].files.Count);
                for (int x = 0; x < burnQueue[i].files.Count; x++)
                {
                    echoDebug(burnQueue[i].files[x].fileName);
                }
            }

            return true;
        }

        private void echoDebug(string text)
        {
            //Console.WriteLine(text);
            if (mainWindow != null) mainWindow.debugEcho(text);
            else
            {
                throw new NullReferenceException("mainWindow in BurnPoolManager is null, debug output failed.");
            }
        }

        private void echoDebugA(string text)
        {
            if (mainWindow != null) mainWindow.debugEchoAsync(text);
            else
            {
                throw new NullReferenceException("mainWindow in BurnPoolManager is null, debug output failed.");
            }
        }

        public void test()
        {
            echoDebug("WE are haveing a test!!");
        }
        

    }
}
