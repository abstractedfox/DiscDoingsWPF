using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
//using System.Threading.Tasks;

using Windows.Storage;

using System.IO;
using Windows.Storage.Pickers;
using System.Windows.Forms;

using System.Text.Json;

using System.Threading;
using System.Collections;
using Windows.ApplicationModel.UserDataTasks;
using System.Runtime.CompilerServices;

namespace DiscDoingsWPF
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 

    //Use this to associate a given error code with an index in the burn pool
    struct ErrorCodeAndIndex
    {
        public BurnPoolManager.ErrorCode resultCode;
        public int index;
    }

    struct LocalConfig
    {

    }

    public class TaskContainer<Task> : ICollection
    {
        List<Task> taskItems = new List<Task>();

        public Task this[int i]
        {
            get { return taskItems[i]; }
            set { taskItems[i] = value; }
        }

        public void Add(Task itemToAdd)
        {
            taskItems.Add(itemToAdd);
        }

        public void RemoveAt(int index)
        {
            try
            {
                taskItems.RemoveAt(index);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        public void CopyTo(Task[] array, int arrayIndex)
        {
            taskItems.CopyTo(array, arrayIndex);
        }

        void ICollection.CopyTo(Array array, int index)
        {
            throw new NotImplementedException();
            //This is the only way to make the compiler happy
        }

        public int Count
        {
            get
            {
                return taskItems.Count;
            }
        }

        //int ICollection.Count => ((ICollection)taskItems).Count;

        public IEnumerator GetEnumerator()
        {
            return ((IEnumerable)taskItems).GetEnumerator();
        }


        public object SyncRoot
        {
            get
            {
                return this; //the documentation says to return the current instance
            }
        }

        public bool IsSynchronized
        {
            get
            {
                return false; //Change this to true if we decide to build locks into this class rather than places where it's used
            }
        }


    }

    public partial class MainWindow : Window
    {
        private string _debugText = "Debug Output\n"; //Append any debug related messages to this string
        private const string _applicationName = "Burn Manager", applicationExtension = "burnlog";
        bool debugWindowOpen = false;
        private List<Task> _burnQueueTasks, _folderAddTasks, _auditTasks;
        private TaskContainer<Task> _fileAddTasks;

        //don't const this, it will be useful to let the user change it later
        //private string _tempBurnFolder = "C:\\Users\\#UserName\\AppData\\Local\\Microsoft\\Windows\\Burn\\Burn1\\";

        public BurnPoolManager BurnPool;
        private BurnPoolManager _lastSavedInstance; //Use to keep track of whether changes have been made

        //This is used by the informUser() function, it's to make those function calls a little more readable
        public enum UserMessages
        {
            DISC_ALREADY_BURNED,
            FILE_AUDIT_FAILED,
            TASKS_PENDING,
            STAGING_PATH_INVALID
        }

        public enum ErrorCode
        {
            SUCCESS,
            SUCCESS_WITH_WARNINGS,
            DIRECTORY_NOT_FOUND,
            FILE_NOT_FOUND,
            UNKNOWN_ERROR
        }


        public MainWindow()
        {
            InitializeComponent();
            _fileAddTasks = new TaskContainer<Task>();
            _burnQueueTasks = new List<Task>();
            _folderAddTasks = new List<Task>();
            _auditTasks = new List<Task>();
            _StartBurnPool();
            _UpdateAllWindowsWhenTasksReachZero();

            InformUser("Obligatory disclaimer: Do not use this work-in-progress software for anything important.\nBy testing this application, you agree that you accept personal responsibility for the integrity of your own data, and that any contributors to this project shall not be liable for any loss, destruction, dissemination, mishandling, corruption, conflagration, defenestration, or cromulation of data belonging to or under the care of you, your employer, or any other party.");
        }

        private void _Test()
        {
            
        }

         
        private async void _OpenDebugWindow(object sender, RoutedEventArgs e)
        {
            /*
            if (debugWindowOpen) //Come back to this later when we fix that edge case memory leak
            {
                for (int i = 0; i < System.Windows.Application.Current.Windows.Count; i++)
                {

                    if (System.Windows.Application.Current.Windows[i].Name == "DebugOutputWindow")
                    {
                        //Application.Current.Windows[i].Show();
                        //MessageBox.Show("We got into the show block");
                        //return;
                    }
                }
            }*/

            var debugWindow = new DebugOutput();
            debugWindow.Owner = this;
            debugWindow.Topmost = false;
            this.Name = "DebugOutputWindow";
            
            debugWindow.Show();
            debugWindowOpen = true;
            
            while (true)
            {
                debugWindow.DebugOutputTextBox.Text = _debugText;
                debugWindow.DebugOutputTaskCounter.Content = "Tasks: " + GetPendingTasks();
                await Task.Delay(500);
            }
            
        }

        private async void _CompareChecksums(object sender, RoutedEventArgs e)
        {
            const string debugName = "MainWindow::compareChecksums():";
            const bool debug = false;
            if (debug) DebugEcho(debugName + "Start");
            List<ErrorCodeAndIndex> badResults = await _AuditFiles();

            if (badResults.Count == 0)
            {
                DebugEcho(debugName + "All files appear normal!");
                return;
            }

            for (int i = 0; i < badResults.Count; i++)
            {
                string outputstring = "Index " + badResults[i].index + " returned " + badResults[i].resultCode.ToString();
                DebugEcho(debugName + outputstring);
            }
            if (debug) DebugEcho(debugName + "Complete");
        }

        //Audit the files in the burn pool against their file system counterparts, seeing that they still exist and have the same
        //checksums.
        //Also assigns these values to the 'status' property for each file.
        private async Task<List<ErrorCodeAndIndex>> _AuditFiles()
        {
            const string debugName = "MainWindow::auditFiles():";
            const bool debug = false;
            DebugEcho(debugName + "Starting asynchronous checksum check.");
            object lockObject = new object();

            List<ErrorCodeAndIndex> badResults = new List<ErrorCodeAndIndex>();
            //List<Task> taskQueue = new List<Task>();

            List<int> iValues = new List<int>(); //for debugging only

            for (int i = 0; i < BurnPool.AllFiles.Count; i++)
            {
                ErrorCodeAndIndex temp = new ErrorCodeAndIndex();
                int copyI = i;
                _auditTasks.Add(Task.Run(() => {
                    temp.resultCode = BurnPool.CompareChecksumToFileSystem(copyI);
                    temp.index = copyI;
                    lock (lockObject)
                    {
                        iValues.Add(copyI);
                    }
                }));
                if (temp.resultCode != BurnPoolManager.ErrorCode.FILES_EQUAL)
                {
                    lock (lockObject)
                    {
                        badResults.Add(temp);
                        BurnPool.SetErrorCode(i, temp.resultCode);
                    }
                }
            }

            await Task.Run(() =>
            {
                while (GetPendingTasks() > 0) ;

                //this block is for debugging only
                {
                    DebugEcho(debugName + "Starting i value check");
                    for (int i = 0; i < iValues.Count; i++)
                    {
                        for (int x = i + 1; x < iValues.Count; x++)
                        {
                            if (iValues[i] == iValues[x]) DebugEcho(debugName + "Duplicate i value found.");
                        }
                    }
                    DebugEcho(debugName + "i value check done");
                }

                return badResults;
            });


            if (debug) DebugEcho(debugName + "Done");
            return badResults;

        }

        
        //Goes to the ListBox in the burn view
        private void _ChooseBurnList(object sender, SelectionChangedEventArgs args)
        {
            
        }
        
        
        //Called when an item in AllFilesListBox is clicked
        private void _ChooseFileList(object sender, SelectionChangedEventArgs args)
        {
            _UpdateMainScreenFileDetails();
        }
        

        //Updates the file details attributes on the main screen based on whatever file is selected in the file picker.
        private void _UpdateMainScreenFileDetails()
        {
            const string detailsName = "No files selected", detailsPath = "Location: ", detailsSize = "Size: ";
            const string debugName = "updateMainScreenFileDetails():";
            const bool debug = false;
            long fileSizeTally = 0;

            if (AllFilesListBox.SelectedItems.Count == 0)
            {
                AllFiles_DetailsName.Content = detailsName;
                AllFiles_DetailsPath.Content = detailsPath;
                AllFiles_DetailsSize.Content = detailsSize;
                return;
            }

            if (AllFilesListBox.SelectedItems.Count > 1)
            {
                AllFiles_DetailsName.Content = "Multiple files selected";
            } 
            else
            {
                AllFiles_DetailsName.Content = BurnPool.AllFiles[BurnPool.FindFileByFullPath(AllFilesListBox.SelectedItem.ToString())].FileName;
            }

            //Note: The AllFilesListBox should always display items in the same order as they are in the burnpool.allFiles array
            for (int i = 0; i < AllFilesListBox.SelectedItems.Count; i++)
            {
                try
                {
                    fileSizeTally += BurnPool.AllFiles[BurnPool.FindFileByFullPath(AllFilesListBox.SelectedItems[i].ToString())].Size;
                    if (i == 0)
                    {
                        AllFiles_DetailsPath.Content = _GetDirectoryFromPath(AllFilesListBox.SelectedItems[i].ToString());
                    }
                    else
                    {
                        if (_GetDirectoryFromPath(AllFilesListBox.SelectedItems[i].ToString()) != AllFiles_DetailsPath.Content.ToString())
                        {
                            if (debug)
                            {
                                DebugEcho(debugName + "The string [" + _GetDirectoryFromPath(AllFilesListBox.SelectedItems[i].ToString()) +
                                    "] != [" + AllFiles_DetailsPath.Content + "]");
                            }
                            AllFiles_DetailsPath.Content = "Files from multiple directories selected";
                        }
                    }
                }
                catch (NullReferenceException)
                {
                    string debugOut = debugName + "Null reference exception when looking for " + AllFilesListBox.SelectedItems[i].ToString() +
                        " in burnpool.";
                    DebugEcho(debugOut);
                    System.Windows.MessageBox.Show(debugOut);
                    return;
                }
            }

            AllFiles_DetailsSize.Content = detailsSize + fileSizeTally.ToString() + " bytes";
        }

        //Initializes the currently loaded burnpool and window contents
        private void _StartBurnPool()
        {
            //string[] directories = { @"C:\Users\coldc\Downloads", @"C:\Users\coldc\Documents\testing data compare\files of various sizes" };
            const string hello = "Welcome to " + _applicationName + "!\nYou are currently experiencing build 0.1, the first proof of concept build.\nAll core functionality is present, but it's not very pretty yet.\nPlease test it out and feel free to inform me of any bugs, or questionable behavior.\nView the Log to see this message again and usage instructions. Have fun!";
            const string instructions = "\n\nUsage instructions:\nStart by adding any files you would like in your backup using \"Add Files!\" or \"Add a Directory!\"\nAn MD5 checksum will be made of each file as it is added. To recheck these checksums, you can click the \"Audit All Files!\" button. Keep in mind that these operations may be slow when processing many files.\nDouble-click a file in the 'All files' list to see more details about it.\"" +
                "\nThe 'Burn view' tab is where it will divide the files out for you. Put in the size of your backup media in bytes and click 'Generate individual burns.' \nOnce it's done, your files will have been distributed to fit efficiently across however many volumes are needed. Hopefully, this will be as few volumes as possible (further algorithm tweaks may come in the future!)\nThe 'Stage this burn' button will stage the burn into your Windows temporary burn directory, and include a text file listing every file that is going into that burn plus some attributes.\nIf there isn't room for this text file, the smallest file(s) will be removed from that burn to make space.\nOnce your volume is burned, cilck 'Mark this as burned' to move it to the discs burned tab. \nEach file will have its hash rechecked against the file system when you do this to ensure integrity!\nOnce a disc is marked as burned, files belonging to that burn can't be removed from the main file list, and that burn cannot be deleted, unless it's unmarked.";
            DebugEcho("Initializing burnpool");
            BurnPool = new BurnPoolManager(this);
            VolumeSizeTextInput.Text = "";
            _UpdateAllWindows();
            ErrorCode burnDirsSuccess = _GetBurnDirectories();
            if (burnDirsSuccess != ErrorCode.SUCCESS)
            {
                DebugEcho("Warning: The system's burn directories could not be found automatically. When staging files to be burned, it may be necessary to manually input the path of your system's temporary burn folder.");

            }

            DebugEcho("Using file version " + BurnPoolManager._formatVersion);
            DebugEcho(hello);
            InformUser(hello);
            DebugEcho(instructions);

            _lastSavedInstance = new BurnPoolManager(BurnPool);
        }

        private async Task _GenerateBurnListsForPoolAsyncA(long volumeSize)
        {
            const bool debug = false;
            if (debug) DebugEcho("generateBurnListsForPoolAsyncA: Start");
            BurnPoolManager.OneBurn aBurn = new BurnPoolManager.OneBurn();

            await Task.Run(() =>
            {
                while (BurnPool.GenerateOneBurn(volumeSize));
                //yes this works; generateOneBurn returns false once it can no longer generate a OneBurn
            });
        }


        //Send any debugging-related text here
        public void DebugEcho(string text)
        {
            _debugText += text += "\n";
        }

        //For output that isn't strictly debug related & may be useful to users
        public void LogOutput(string text)
        {
            _debugText += text += "\n";
        }


        public async void DebugEchoAsync(string text)
        {
            await Task.Run(() => { DebugEcho(text); });
        }

        public void InformUser(string text)
        {
            System.Windows.MessageBox.Show(text);
        }

        public void InformUser(UserMessages message)
        {
            string usertext = message.ToString();
            switch (message)
            {
                case UserMessages.DISC_ALREADY_BURNED:
                    usertext = "This disc has been burned; files cannot be removed.";
                    break;
                case UserMessages.FILE_AUDIT_FAILED:
                    usertext = "File discrepancies were detected, please check the log.";
                    break;
                case UserMessages.TASKS_PENDING:
                    usertext = "There are still tasks in progress. Please wait until tasks finish.";
                    break;
                case UserMessages.STAGING_PATH_INVALID:
                    usertext = "Please choose or input a valid path to stage this burn.";
                    break;
            }
            System.Windows.MessageBox.Show(usertext);
        }

        //Show details for a FileProps in the main list box
        private void _FileViewListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            const string debugName = "MainWindow::FileViewListBox_MouseDoubleClick():";
            if (AllFilesListBox.SelectedItems.Count != 1)
            {
                return;
            }

            int? filePropsToGet = BurnPool.FindFileByFullPath(AllFilesListBox.SelectedItem.ToString());
            if (filePropsToGet == null)
            {
                DebugEcho(debugName + "Invalid selection");
                return;
            }

            var fileViewDetails = new FilePropsViewDetails(ref BurnPool, this, (int)filePropsToGet);
            fileViewDetails.Owner = this;
            fileViewDetails.Topmost = false;

            fileViewDetails.Show();
        }

        //Show details for a OneBurn in the burn view list box
        private void _BurnViewListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            const string debugName = "MainWindow::BurnViewListBox_MouseDoubleClick():";
            int? oneBurnToGet = 0;

            if (BurnViewListBox.SelectedItems.Count == 0) return;

            try
            {
                oneBurnToGet = BurnPool.GetBurnQueueFileByName(BurnViewListBox.SelectedItem.ToString());
            }
            catch (NullReferenceException)
            {
                DebugEcho(debugName + "BurnViewListBox.SelectedItem.ToString() Null reference exception");
            }

            if(oneBurnToGet == null)
            {
                DebugEcho("BurnViewListBox: Invalid selection");
                return;
            }

            var burnViewDetails = new OneBurnViewDetails(ref BurnPool, (int)oneBurnToGet, this);
            burnViewDetails.Owner = this;
            burnViewDetails.Topmost = false;

            burnViewDetails.Show();
        }

        private void _BurnedDiscsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            int? oneBurnToGet = 0;
            oneBurnToGet = BurnPool.GetBurnQueueFileByName(BurnedDiscsListBox.SelectedItem.ToString());

            if (oneBurnToGet == null)
            {
                DebugEcho("BurnViewListBox: Invalid selection");
                return;
            }

            var burnViewDetails = new OneBurnViewDetails(ref BurnPool, (int)oneBurnToGet, this);
            burnViewDetails.Owner = this;
            burnViewDetails.Topmost = false;

            burnViewDetails.Show();
        }

        //The button that instructs the program to calculate burn lists based on the files added
        private async void _CalculateBurnListButtonClick(object sender, RoutedEventArgs e)
        {
            long volumeSize = 0;
            try
            {
                volumeSize = long.Parse(VolumeSizeTextInput.Text);
            }
            catch (ArgumentNullException)
            {
                DebugEcho("CalculateBurnListButtonClick ArgumentNullException: VolumeSizeTextInput.Text == null");
                System.Windows.MessageBox.Show("Invalid volume size, please try again", _applicationName);
                return;
            }
            catch (FormatException)
            {
                DebugEcho("CalculateBurnListButtonClick FormatException: VolumeSizeTextInput.Text does not appear to be a valid int");
                System.Windows.MessageBox.Show("Invalid volume size, please try again", _applicationName);
                return;
            }
            catch (OverflowException)
            {
                DebugEcho("CalculateBurnListButtonClick FormatException: Volume size is greater than Int64.MaxValue");
                System.Windows.MessageBox.Show("Volume size is too large. What kind of media are you using??", _applicationName);
                return;
            }
            catch (Exception)
            {
                DebugEcho("CalculateBurnListButtonClick FormatException: Unknown exception");
                System.Windows.MessageBox.Show("Unknown exception", _applicationName);
                return;
            }
            if (GetPendingTasks() == 0)
            {
                _burnQueueTasks.Add(_GenerateBurnListsForPoolAsyncA(volumeSize));
                _UpdateAllWindowsWhenTasksComplete();
                _CleanUpTaskLists();
            }
            else
            {
                _OperationsInProgressDialog();
            }

        }


        //Button handler to stage a burn
        private void _StageThisBurnButtonClick(object sender, RoutedEventArgs e)
        {
            const string debugName = "MainWindow::StageThisBurnButtonClick():", friendlyName = "";

            if (BurnViewListBox.SelectedItems.Count == 0)
            {
                string debugtext = "Please select a burn to stage.";
                System.Windows.MessageBox.Show(debugtext);
                return;
            }

            int? oneBurnToStage = 0;

            try
            {
                oneBurnToStage = BurnPool.GetBurnQueueFileByName(BurnViewListBox.SelectedItem.ToString());
            }
            catch (NullReferenceException)
            {
                DebugEcho(debugName + "Null reference thrown when attempting burnpool.getBurnQueueFileByName. Was a OneBurn" +
                    " selected in the ListView?");
                return;
            }

            if (oneBurnToStage == null)
            {
                DebugEcho(debugName + "The OneBurn \"" + BurnViewListBox.SelectedItem.ToString() + "\" was not found in burnQueue.");
                return;
            }


            ErrorCode a = _StageABurn((int)oneBurnToStage, true);
            if (a == ErrorCode.SUCCESS) LogOutput(friendlyName + "The burn " + BurnPool.BurnQueue[(int)oneBurnToStage].Name + " was staged with the result code " + a.ToString() + ".");
            else
            {
                LogOutput(friendlyName + "The burn " + BurnPool.BurnQueue[(int)oneBurnToStage].Name + " was not staged, error " + a.ToString() + " was produced.");
            }
        }

        //Stage the contents of a OneBurn into the windows burn directory
        //includeBurnRecord = include a text document of this OneBurn's files and checksums
        private ErrorCode _StageABurn(int burnQueueIndex, bool includeBurnRecord)
        {
            const string debugName = "MainWindow::StageABurn():", friendlyName = debugName;
            const bool debug = true;

            string? tempBurnFolder = StagingPathComboBox.Text.ToString();

            if (tempBurnFolder == null)
            {
                InformUser(UserMessages.STAGING_PATH_INVALID);
                LogOutput(friendlyName + "A burn staging path was not entered.");
                return ErrorCode.DIRECTORY_NOT_FOUND;
            }

            if (!Directory.Exists(tempBurnFolder))
            {
                InformUser(UserMessages.STAGING_PATH_INVALID);
                LogOutput(friendlyName + "The path \"" + tempBurnFolder + "\" is invalid.");
                return ErrorCode.DIRECTORY_NOT_FOUND;
            }

            if (debug) DebugEcho(debugName + "Staging to directory: " + tempBurnFolder);

            //string userName = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            //userName = userName.Substring(userName.IndexOf("\\") + 1);
            //tempBurnFolder = tempBurnFolder.Replace("#UserName", userName);


            if (includeBurnRecord)
            {
                if (debug) DebugEcho(debugName + "Including burn record");
                string burnRecord = BurnPool.BurnQueue[burnQueueIndex].ToString();
                FileInfo newFile = new FileInfo(tempBurnFolder + "File Catalog.txt");

                if (debug) DebugEcho(debugName + "Space remaining in OneBurn:" + BurnPool.BurnQueue[burnQueueIndex].SpaceRemaining +
                    "\nSpace occupied by file output:" + System.Text.ASCIIEncoding.Unicode.GetByteCount(burnRecord));

                while (System.Text.ASCIIEncoding.Unicode.GetByteCount(burnRecord) > BurnPool.BurnQueue[burnQueueIndex].SpaceRemaining)
                {
                    if (debug) DebugEcho(debugName + "Removing file " + BurnPool.BurnQueue[burnQueueIndex].Files[BurnPool.BurnQueue[burnQueueIndex].Files.Count - 1].FileName +
                        " with the size " + BurnPool.BurnQueue[burnQueueIndex].Files[BurnPool.BurnQueue[burnQueueIndex].Files.Count - 1].Size +
                        " to make space for the output of size " + System.Text.ASCIIEncoding.Unicode.GetByteCount(burnRecord));


                    BurnPool.RemoveFileFromOneBurn(burnQueueIndex, BurnPool.BurnQueue[burnQueueIndex].Files.Count - 1);
                    burnRecord = BurnPool.BurnQueue[burnQueueIndex].ToString();

                }

                using (StreamWriter streamtime = newFile.CreateText())
                {
                    streamtime.WriteLine(burnRecord);
                }
            }

            List<FileInfo> filesToCopy = new List<FileInfo>();
            bool foundErrors = false;
            for (int i = 0; i < BurnPool.BurnQueue[burnQueueIndex].Files.Count; i++)
            {
                FileInfo newFile = new FileInfo(BurnPool.BurnQueue[burnQueueIndex].Files[i].OriginalPath);
                if (!newFile.Exists)
                {
                    BurnPool.SetErrorCode(BurnPool.FindFileByFullPath(BurnPool.BurnQueue[burnQueueIndex].Files[i].OriginalPath), BurnPoolManager.ErrorCode.FILE_NOT_FOUND_IN_FILESYSTEM);
                    foundErrors = true;
                }
                else
                {
                    filesToCopy.Add(newFile);
                }
            }

            if (foundErrors)
            {
                return ErrorCode.FILE_NOT_FOUND;
            }
            else
            {
                

                for (int i = 0; i < filesToCopy.Count; i++)
                {
                    string copyName = tempBurnFolder + filesToCopy[i].Name;
                    try
                    {
                        filesToCopy[i].CopyTo(copyName, false);
                    }
                    catch (IOException e)
                    {
                        LogOutput(friendlyName + "IO Exception was thrown when copying \"" + filesToCopy[i].FullName +
                            "\". Sometimes copy discrepancies can occur when copying from a network volume. If a \"File Already Exists\" exception is thrown when the directory was initially empty, this can be regarded. Full exception text: \n"
                            + e);
                    
                    }
                    catch(Exception e)
                    {
                        InformUser(friendlyName + "Exception \"" + e + "\" was thrown when copying " + filesToCopy[i].FullName + 
                            " to the temporary burn path at " + tempBurnFolder);
                        return ErrorCode.UNKNOWN_ERROR;
                    }
                }
            }

            
            return ErrorCode.SUCCESS;
        }


        //Called when the user instructs the program to mark a OneBurn as burned to disc
        private void _MarkBurnedButtonClick(object sender, RoutedEventArgs e)
        {
            const string debugName = "MainWindow::MarkBurnedButtonClick():";

            if (BurnViewListBox.SelectedItems.Count == 0)
            {
                string debugtext = "Please select a burn to mark as burned.";
                System.Windows.MessageBox.Show(debugtext);
                return;
            }

            if (GetPendingTasks() > 0)
            {
                InformUser(UserMessages.TASKS_PENDING);
            }

            int? oneBurnToCommit = 0;

            try
            {
                oneBurnToCommit = BurnPool.GetBurnQueueFileByName(BurnViewListBox.SelectedItem.ToString());
            }
            catch (NullReferenceException)
            {
                DebugEcho(debugName + "Null reference thrown when attempting burnpool.getBurnQueueFileByName. Was a OneBurn" +
                    " selected in the ListView?");
                return;
            }

            if (oneBurnToCommit == null)
            {
                DebugEcho(debugName + "The OneBurn \"" + BurnViewListBox.SelectedItem.ToString() + "\" was not found in burnQueue.");
                return;
            }

            BurnPoolManager.ErrorCode result = BurnPool.CommitOneBurn((int)oneBurnToCommit, true);

            if (result != BurnPoolManager.ErrorCode.SUCCESS)
            {
                InformUser(UserMessages.FILE_AUDIT_FAILED);
            }

            _UpdateAllWindowsWhenTasksComplete();
        }

        private void _MarkUnburnedButtonClick(object sender, RoutedEventArgs e)
        {
            const string debugName = "MainWindow::MarkUnburnedButtonClick():";

            if (BurnedDiscsListBox.SelectedItems.Count == 0)
            {
                string debugtext = "Please select a burn to unmark as burned.";
                InformUser(debugtext);
                return;
            }


            MessageBoxResult result = System.Windows.MessageBox.Show("Are you sure you want to mark the burn list \"" +
                BurnedDiscsListBox.SelectedItem.ToString() + "\" as unburned?",
                    _applicationName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                return;
            }

            int? oneBurnToUncommit = 0;

            try
            {
                oneBurnToUncommit = BurnPool.GetBurnQueueFileByName(BurnedDiscsListBox.SelectedItem.ToString());
            }
            catch (NullReferenceException)
            {
                DebugEcho(debugName + "Null reference thrown when attempting burnpool.getBurnQueueFileByName. Was a OneBurn" +
                    " selected in the ListView?");
                return;
            }

            if (oneBurnToUncommit == null)
            {
                DebugEcho(debugName + "The OneBurn \"" + BurnedDiscsListBox.SelectedItem.ToString() + "\" was not found in burnQueue.");
                return;
            }

            BurnPool.UncommitOneBurn((int)oneBurnToUncommit);

            _UpdateAllWindows();
        }

        private void _RemoveBurnButtonClick(object sender, RoutedEventArgs e)
        {
            const string debugName = "MainWindow::RemoveBurnButtonClick:";
            if (BurnViewListBox.SelectedItems.Count == 0)
            {
                string debugtext = "Please select a burn to remove.";
                InformUser(debugtext);
                return;
            }

            MessageBoxResult result = System.Windows.MessageBox.Show("Are you sure you want to remove the burn list \"" +
                BurnViewListBox.SelectedItem.ToString() + "\"?",
                    _applicationName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                return;
            }

            if (GetPendingTasks() > 0)
            {
                InformUser(UserMessages.TASKS_PENDING);
                return;
            }

            try
            {
                BurnPool.DeleteOneBurn((int)BurnPool.GetBurnQueueFileByName(BurnViewListBox.SelectedItem.ToString()));
            }
            catch (NullReferenceException)
            {
                DebugEcho(debugName + "Null reference exception: Null returned when attempting to find OneBurn titled \"" +
                    BurnViewListBox.SelectedItem.ToString() + "\"");
            }

            _UpdateAllWindows();
        }


        //Opens the file picker and adds any files chosen to the burn pool.
        //At the moment this function does not check for outstanding fileAddTasks as it seems to work fine adding more files to
        //the list while operations are pending. However, it does check for burnQueueTasks
        private async void _OpenFilePicker(object sender, RoutedEventArgs e)
        {
            const bool debugVerbose = false, msgBoxes = false, logging = true;
            const string debugName = "OpenFilePicker:", friendlyName = "";
            FileOpenPicker picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.List;
            //picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");
            
            
            
            if (_GetBurnQueueTasks() > 0)
            {
                InformUser("Some operations are still in progress. Please wait for operations " +
                    "to finish before adding more files.");
                return;
            }
            

            var win32moment = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr handle = win32moment.Handle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
            IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
            int filesSelected = files.Count;

            if (logging) LogOutput(friendlyName + "Preparing to add " + filesSelected + " files.");
            if (msgBoxes) System.Windows.MessageBox.Show("File picker happened");


            if (files.Count == 0)
            {
                return;
            }


            if (debugVerbose)
            {
                DebugEcho(debugName + "IReadOnlyList<StorageFile> files is now populated");
            }

            bool issues = false;

            await Task.Run(() => {
                //int filesPrior = burnpool.allFiles.Count;
                List<BurnPoolManager.FileProps> newFileBuffer = new List<BurnPoolManager.FileProps>();
                if (msgBoxes) System.Windows.MessageBox.Show("task START");
                foreach (StorageFile file in files)
                {
                    //burnpool.addFile(file);
                    try
                    {
                        _fileAddTasks.Add(BurnPool.AddFileAsync(file));
                    }
                    catch
                    {
                        LogOutput(debugName + "Exception while adding files to the burnpool.");
                        //System.Windows.MessageBox.Show(debugName + "Exception while adding files to the burnpool.", applicationName);
                        filesSelected--;
                        issues = true;
                    }
                }
            });

            if (debugVerbose)
            {
                DebugEcho(debugName + "All files have been added to burnpool");
            }

            _UpdateAllWindowsWhenTasksComplete();
            _CleanUpTaskLists();

            if (logging)
            {
                if (!issues)
                {
                    LogOutput(friendlyName + "Successfully added " + filesSelected + " files to the queue.");
                }
                else
                {
                    LogOutput(friendlyName + "Successfully added " + filesSelected + " files to the queue. There were some errors, so not all files were added.");
                }
                
            }
            

        }

        private async void _OpenFolderPicker(object sender, RoutedEventArgs e)
        {
            const bool debugVerbose = false, logging = true;
            const string debugName = "OpenFolderPicker:", friendlyName = "";
            FolderPicker picker = new FolderPicker();
            picker.ViewMode = PickerViewMode.List;
            //picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");



            if (_GetBurnQueueTasks() > 0)
            {
                //informUser("Some operations are still in progress. Please wait for operations " +
                //    "to finish before adding more files.");
                InformUser(UserMessages.TASKS_PENDING);
                return;
            }



            var win32moment = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr handle = win32moment.Handle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
            Windows.Storage.StorageFolder theFolder = await picker.PickSingleFolderAsync();

            if (theFolder == null)
            {
                if (debugVerbose) DebugEcho(debugName + "theFolder = null, this is normal if the user clicked cancel.");
                return;
            }

            if (debugVerbose) DebugEcho(debugName + "Adding folder " + theFolder.Name);
            if (logging) LogOutput(friendlyName + "Adding folder " + theFolder.Name);
            IReadOnlyList<IStorageItem> foldercontents = await theFolder.GetItemsAsync();
            _folderAddTasks.Add(_addFolderRecursive(foldercontents, true));

            
            _UpdateAllWindowsWhenTasksComplete();
            _CleanUpTaskLists();

            if (debugVerbose)
            {
                DebugEcho(debugName + "All files have been added to burnpool");
            }


        }

        private async Task<Task> _addFolderRecursive(IReadOnlyList<IStorageItem> foldercontents, bool recursion)
        {
            const string debugName = "MainWindow::addFolderRecursive():", friendlyName = "";
            const bool debugVerbose = false, logging = true;
            
            //List<StorageFile> files = new List<StorageFile>();
            //List<StorageFolder> folders = new List<StorageFolder>();

            //keyword asdf: new behavior here, might break
            return Task.Run(() => { 
                foreach (IStorageItem item in foldercontents)
                {
                    if (item.IsOfType(StorageItemTypes.File))
                    {
                        //files.Add((StorageFile)item);
                        _fileAddTasks.Add(BurnPool.AddFileAsync((StorageFile)item));
                    }
                    if (item.IsOfType(StorageItemTypes.Folder) && recursion)
                    {
                        if (logging) LogOutput(friendlyName + "Adding the folder \"" + item.Name + "\" to the task queue.");
                        _StartNewFolderTask((StorageFolder)item);
                    }
                }
            });


            //keyword asdf: if the above didn't break, this can be removed
            /*
            if (debugVerbose)
            {
                DebugEcho(debugName + "IReadOnlyList<StorageFile> files is now populated");
            }
            if (logging)
            {
                LogOutput(friendlyName + "Adding " + files.Count + " files to the task queue.");
            }



            return Task.Run(() => {

                foreach (StorageFile file in files)
                {
                    try
                    {
                        _fileAddTasks.Add(BurnPool.AddFileAsync(file));
                    }
                    catch
                    {
                        DebugEcho(debugName + "Exception while adding files to the burnpool.");
                        System.Windows.MessageBox.Show(debugName + "Exception while adding files to the burnpool.", _applicationName);
                    }
                }
                if (files.Count > 0) {
                    LogOutput(friendlyName + "Completed adding the folder \"" + files[0].Path.Substring(0, files[0].Path.LastIndexOf("\\")) + "\" to the task queue.");
                }
            });
            */

            if (debugVerbose) DebugEcho(debugName + "Tasks completed");
        }

        private async void _StartNewFolderTask(StorageFolder folder)
        {
            bool logging = true;
            IReadOnlyList<IStorageItem> newfolder = await folder.GetItemsAsync();
            _fileAddTasks.Add(_addFolderRecursive(newfolder, true));

        }

        //Handler for the button that removes files
        private void _AllFiles_RemoveFileButtonClick(object sender, RoutedEventArgs e)
        {
            if (AllFilesListBox.SelectedItems.Count == 0)
            {
                System.Windows.MessageBox.Show("Please select at least one file to remove.");
                return;
            }
            for (int i = 0; i < AllFilesListBox.SelectedItems.Count; i++)
            {
                BurnPool.RemoveFile(AllFilesListBox.SelectedItems[i].ToString());
            }
            _UpdateAllWindowsWhenTasksComplete();
        }

        //Update the main file view
        private void _UpdateMainScreenFileView()
        {
            const bool debug = true;
            const string debugName = "updateMainScreenFileView():";
            AllFilesListBox.Items.Clear();
            for (int i = 0; i < BurnPool.AllFiles.Count; i++)
            {
                AllFilesListBox.Items.Add(BurnPool.AllFiles[i].OriginalPath);
            }


        }

        //Populate the burn window with whatever is in burnpool.burnQueue. Always blanks the burn window first.
        private void _PopulateBurnWindow()
        {
            BurnViewListBox.Items.Clear();
            for (int i = 0; i < BurnPool.BurnQueue.Count; i++)
            {
                if (BurnPool.BurnQueue[i].TimesBurned == 0)
                {
                    BurnViewListBox.Items.Add(BurnPool.BurnQueue[i].Name);
                }
            }
        }

        private void _PopulateBurnedWindow()
        {
            BurnedDiscsListBox.Items.Clear();
            for (int i = 0; i < BurnPool.BurnQueue.Count; i++)
            {
                if (BurnPool.BurnQueue[i].TimesBurned > 0)
                {
                    BurnedDiscsListBox.Items.Add(BurnPool.BurnQueue[i].Name);
                }
            }
        }

        //Update the data display in all windows
        private void _UpdateAllWindows()
        {
            _UpdateMainScreenFileView();
            _PopulateBurnWindow();
            _PopulateBurnedWindow();
        }

        private async void _UpdateAllWindowsWhenTasksComplete()
        {
            const bool debug = false, msgBoxes = false;
            const string debugName = "MainWindow::updateAllWindowsWhenTasksComplete():";
            if (debug) DebugEcho(debugName + "Start");
            if (msgBoxes) System.Windows.MessageBox.Show(debugName + "Start");

            //while (true)
            //{
                await Task.Run(() =>
                {
                    while (GetPendingTasks() > 0) ;
                });
                if (debug) DebugEcho(debugName + "Updating windows now.");
                if (msgBoxes) System.Windows.MessageBox.Show(debugName + "End");
                _UpdateAllWindows();
                //await Task.Delay(1000);
            //}
        }

        //Different from above; watches the list of pending tasks and calls updateAllWindowsWhenTasksComplete() whenever
        //the list of pending tasks goes from non-zero back to zero
        private async void _UpdateAllWindowsWhenTasksReachZero()
        {
            const bool debug = false;
            const string debugName = "MainWindow::updateAllWindowsWhenTasksReachZero():";
            int lastTasks = 0;
            while (true)
            {
                if (lastTasks > 0 && GetPendingTasks() == 0)
                {
                    if (debug) DebugEcho(debugName + "Activated");
                    _UpdateAllWindowsWhenTasksComplete();
                }
                lastTasks = GetPendingTasks();
                await Task.Delay(250);
            }
        }

        //Null catches are in case the task list changes in size when this is running
        public int GetPendingTasks()
        {
            
            int tasks = 0;
            for (int i = 0; i < _fileAddTasks.Count; i++)
            {
                try
                {
                    if (_fileAddTasks[i] == null) break;
                    if (!_fileAddTasks[i].IsCompleted) tasks++;
                }
                catch (NullReferenceException)
                {
                    break;
                }
            }
            for (int i = 0; i < _burnQueueTasks.Count; i++)
            {
                try
                {
                    if (_burnQueueTasks[i] == null) break;
                    if (!_burnQueueTasks[i].IsCompleted) tasks++;
                }
                catch (NullReferenceException)
                {
                    break;
                }
            }
            for (int i = 0; i < _folderAddTasks.Count; i++)
            {
                try
                {
                    if (_folderAddTasks[i] == null) break;
                    if (!_folderAddTasks[i].IsCompleted) tasks++;
                }
                catch
                {
                    break;
                }
            }
            for (int i = 0; i < _auditTasks.Count; i++)
            {
                try
                {
                    if (_auditTasks[i] == null) break;
                    if (!_auditTasks[i].IsCompleted) tasks++;
                }
                catch
                {
                    break;
                }
            }
            return tasks;
        }

        private int _GetBurnQueueTasks()
        {
            int tasks = 0;
            for (int i = 0; i < _burnQueueTasks.Count; i++)
            {
                try
                {
                    if (_burnQueueTasks[i] == null) break;
                    if (!_burnQueueTasks[i].IsCompleted) tasks++;
                }
                catch (NullReferenceException)
                {
                    break;
                }
            }
            return tasks;
        }

        //Removes all completed tasks from fileAddTasks and burnQueueTasks
        private async void _CleanUpTaskLists()
        {
            const string debugName = "MainWindow::cleanUpTaskLists():";
            const bool debug = false, msgBoxes = false;

            if (debug) DebugEcho(debugName + "Start");

            do
            {
                await Task.Delay(10000);
            }
            while (GetPendingTasks() > 0);

            //The task lists can change in size while this function is running when recursive folder adds are running, so a lot
            //of seemingly arbitrary checks are utilized.

            await Task.Run(() => { 
                bool rangeCheck = false;
                do
                {
                    rangeCheck = false;
                    for (int i = 0; i < _fileAddTasks.Count; i++)
                    {
                        try
                        {
                            if (_fileAddTasks[i] == null) break;
                            if (i < _fileAddTasks.Count && _fileAddTasks[i].IsCompleted)
                            {
                                _fileAddTasks.RemoveAt(i);
                                i = -1;
                            }
                            else if (i > _fileAddTasks.Count)
                            {
                                rangeCheck = true;
                                if (msgBoxes) System.Windows.MessageBox.Show(debugName + "i > fileAddTasks.count");
                                break;
                            }
                        }
                        catch (IndexOutOfRangeException)
                        {
                            rangeCheck = true;
                            if (msgBoxes) System.Windows.MessageBox.Show(debugName + "fileAddTasks IndexOutOfRangeException");
                            break;
                        }

                    }

                    for (int i = 0; i < _burnQueueTasks.Count; i++)
                    {
                        try
                        {
                            if (_burnQueueTasks[i] == null) break;
                            if (i < _burnQueueTasks.Count && _burnQueueTasks[i].IsCompleted)
                            {
                                _burnQueueTasks.RemoveAt(i);
                                i = -1;
                            }
                            else if (i > _burnQueueTasks.Count)
                            {
                                rangeCheck = true;
                                if (msgBoxes) System.Windows.MessageBox.Show(debugName + "i > burnQueueTasks.count");
                                break;
                            }
                        }
                        catch (IndexOutOfRangeException)
                        {
                            rangeCheck = true;
                            if (msgBoxes) System.Windows.MessageBox.Show(debugName + "burnQueueTasks IndexOutOFRangeException");
                            break;
                        }
                    }

                    for (int i = 0; i < _folderAddTasks.Count; i++)
                    {
                        try
                        {
                            if (_folderAddTasks[i] == null) break;
                            if (i < _folderAddTasks.Count && _folderAddTasks[i].IsCompleted)
                            {
                                _folderAddTasks.RemoveAt(i);
                                i = -1;
                            }
                            else if (i > _folderAddTasks.Count)
                            {
                                rangeCheck = true;
                                if (msgBoxes) System.Windows.MessageBox.Show(debugName + "i > folderAddTasks.count");
                                break;
                            }
                        }
                        catch (IndexOutOfRangeException)
                        {
                            rangeCheck = true;
                            if (msgBoxes) System.Windows.MessageBox.Show(debugName + "folderAddTasks IndexOutOfRangeException");
                            break;
                        }

                    }
                }
                while ((GetPendingTasks() == 0 && _fileAddTasks.Count > 0 || _burnQueueTasks.Count > 0 || _folderAddTasks.Count > 0) || rangeCheck == true);
            });

            if (debug) DebugEcho(debugName + "Finish");
        }

        private async void _RefreshWindowsAuto()
        {
            while (true)
            {
                await Task.Run(() =>
                {
                    _UpdateAllWindowsWhenTasksComplete();
                });
                await Task.Delay(500);
            }
        }

        //Pass a full path to a file and get back just the directory it's in
        private string _GetDirectoryFromPath(string path)
        {
            int lastSlash = -1;
            for (int i = path.Length - 1; i > 0; i--)
            {
                if (path[i] == '\\')
                {
                    lastSlash = i;
                    break;
                }
            }
            if (lastSlash == -1)
            {
                DebugEcho("getDirectoryFromPath: Path " + path + " appears to be invalid.");
                return "";
            }
            return (path.Substring(0, lastSlash + 1));
        }

        //Detect whether changes have been made since the file was last saved, or since a new file was created
        private bool _ChangesMade()
        {
            if (BurnPool == _lastSavedInstance) return false;
            return true;
        }

        private void _SaveFileDialog()
        {
            const string debugName = "saveFileDialog:";
            string serialized = "";
            try
            {
                serialized = JsonSerializer.Serialize(BurnPool);
            }
            catch
            {
                DebugEcho("FileSave_Click: Exception thrown using JsonSerializer.Serialize()");
                System.Windows.MessageBox.Show("FileSave_Click: Exception thrown using JsonSerializer.Serialize()");
                return;
            }

            if (serialized == null)
            {
                DebugEcho(debugName + "serialized == null");
                return;
            }

            try
            {
                Stream aStream;
                SaveFileDialog saveDialog = new SaveFileDialog();

                saveDialog.Filter = "*." + applicationExtension + "|All Files(*.*)";
                saveDialog.DefaultExt = applicationExtension;

                if (saveDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if ((aStream = saveDialog.OpenFile()) != null)
                    {
                        byte[] toBytes = new byte[serialized.Length];
                        for (int i = 0; i < serialized.Length; i++) toBytes[i] = (byte)serialized[i];

                        aStream.Write(toBytes);
                        aStream.Close();
                        _lastSavedInstance = new BurnPoolManager(BurnPool);
                    }
                    else
                    {
                        DebugEcho(debugName + "saveDialog.OpenFile() did not != null");
                    }
                }
                else
                {
                    DebugEcho(debugName + "saveDialog.ShowDialog() did not == System.Windows.Forms.DialogResult.OK");
                }
            }
            catch
            {
                DebugEcho(debugName + "Unknown exception");
            }
        }

        private void _OperationsInProgressDialog()
        {
            InformUser(UserMessages.TASKS_PENDING);
        }

                    //Top menu bar items start below


        private void _FileSave_Click(object sender, RoutedEventArgs e)
        {
            const string debugName = "FileSave_Click:";
            if (GetPendingTasks() > 0)
            {
                _OperationsInProgressDialog();
                return;
            }
            _SaveFileDialog();
        }

        private void _New_Click(object sender, RoutedEventArgs e)
        {
            const string debugName = "New_Click():";

            if (GetPendingTasks() > 0)
            {
                _OperationsInProgressDialog();
                return;
            }


            if (_ChangesMade())
            {
                MessageBoxResult result = System.Windows.MessageBox.Show("Changes have been made. Do you want to save?",
                    _applicationName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _SaveFileDialog();
                }
                if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            DebugEcho("New file start");
            _StartBurnPool();
        }

        

        private void _MixedUseButton(object sender, RoutedEventArgs e)
        {
            //updateAllWindowsWhenTasksComplete();
            //cleanUpTaskLists();

            _GetBurnDirectories();
        }

        private async void _FileOpen_Click(object sender, RoutedEventArgs e)
        {
            const bool debugVerbose = false;
            const string debugName = "FileLoad_Click:";

            if (GetPendingTasks() > 0)
            {
                _OperationsInProgressDialog();
                return;
            }

            if (_ChangesMade())
            {
                MessageBoxResult result = System.Windows.MessageBox.Show("Changes have been made. Do you want to save?", 
                    _applicationName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    _SaveFileDialog();
                }
                if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            try
            {
                FileOpenPicker picker = new FileOpenPicker();
                picker.ViewMode = PickerViewMode.List;
                //picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add("." + applicationExtension);


                var win32moment = new System.Windows.Interop.WindowInteropHelper(this);
                IntPtr handle = win32moment.Handle;
                WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
                StorageFile file = await picker.PickSingleFileAsync();

                if (file == null)
                {
                    DebugEcho(debugName + "FileOpenPicker result is null. This is expected behavior if the user clicked cancel.");
                    return;
                }

                try
                {
                    if (file.FileType != "." + applicationExtension)
                    {
                        System.Windows.MessageBox.Show("File extension " + file.FileType + " is invalid.");
                    }
                    string serializedJson = await Windows.Storage.FileIO.ReadTextAsync(file);
                    if (serializedJson == null)
                    {
                        DebugEcho(debugName + "Null returned from await Windows.Storage.FileIO.ReadTextAsync");
                        return;
                    }

                    BurnPool = new BurnPoolManager(this);

                    try
                    {
                        BurnPool = JsonSerializer.Deserialize<BurnPoolManager>(serializedJson);
                    }
                    catch (ArgumentNullException)
                    {
                        const string errortext = debugName + "JsonSerializer.Deserialize<BurnPoolManager>(serializedJson): serializedJson = null";
                        DebugEcho(errortext);
                        System.Windows.MessageBox.Show(errortext);
                        return;
                    }
                    catch (JsonException)
                    {
                        const string errortext = debugName + "JsonSerializer.Deserialize<BurnPoolManager>(serializedJson): JsonException." +
                            " File may be formatted incorrectly or corrupt.";
                        DebugEcho(errortext);
                        System.Windows.MessageBox.Show(errortext);
                        return;
                    }
                    catch
                    {
                        const string errortext = debugName + "JsonSerializer.Deserialize<BurnPoolManager>(serializedJson): Unknown exception.";
                        DebugEcho(errortext);
                        System.Windows.MessageBox.Show(errortext);
                        return;
                    }
                    BurnPool.MainWindow = this;
                    _lastSavedInstance = new BurnPoolManager(BurnPool);
                    _UpdateAllWindows();
                }
                catch
                {
                    DebugEcho(debugName + "Exception thrown when deserializing");
                }
            }
            catch
            {
                DebugEcho(debugName + "Exception thrown when initializing FileOpenPicker");
                return;
            }


        }

        //Find the burn directories on the user's system and populate the ComboBox with them.
        //Systems with multiple drives will sometimes have multiple burn directories
        private ErrorCode _GetBurnDirectories()
        {
            const string debugName = "MainWindow::getBurnDirectories():";
            const bool debug = false;

            string rootBurnPath = "C:\\Users\\#UserName\\AppData\\Local\\Microsoft\\Windows\\Burn\\";
            string userName = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            userName = userName.Substring(userName.IndexOf("\\") + 1);
            rootBurnPath = rootBurnPath.Replace("#UserName", userName);

            if (!Directory.Exists(rootBurnPath))
            {
                return ErrorCode.DIRECTORY_NOT_FOUND;

            }
            string []burnDirs = Directory.GetDirectories(rootBurnPath, "burn*");

            for (int i = 0; i < burnDirs.Length; i++)
            {
                burnDirs[i] += "\\";
                StagingPathComboBox.Items.Add(burnDirs[i]);
            }

            StagingPathComboBox.SelectedItem = StagingPathComboBox.Items[0];

            return ErrorCode.SUCCESS;

        }
    }
}
