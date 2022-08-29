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
using System.Threading.Tasks;

using Windows.Storage;
//using Windows.Foundation; //i think this may be necessary to use IAsyncOperation as part of the file picker 
using System.IO;
using Windows.Storage.Pickers;
using System.Windows.Forms;

using System.Text.Json;

using System.Threading;


namespace DiscDoingsWPF
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 

    //Use this to associate a given error code with an index in the burn pool
    struct errorCodeAndIndex
    {
        public BurnPoolManager.errorCode resultCode;
        public int index;
    }

    struct localConfig
    {

    }

    public partial class MainWindow : Window
    {
        string debugText = "Debug Output\n"; //Append any debug related messages to this string
        bool debugWindowOpen = false;
        List<Task> fileAddTasks, burnQueueTasks, folderAddTasks;
        int tasklimit = 10;

        string tempBurnFolder = "C:\\Users\\#UserName\\AppData\\Local\\Microsoft\\Windows\\Burn\\Burn1\\";

        public BurnPoolManager burnpool;
        private BurnPoolManager lastSavedInstance; //Use to keep track of whether changes have been made

        //This is used by the informUser() function, it's to make those function calls a little more readable
        public enum userMessages
        {
            DISC_ALREADY_BURNED,
            FILE_AUDIT_FAILED
        }

        public enum errorCode
        {
            SUCCESS,
            SUCCESS_WITH_WARNINGS,
            DIRECTORY_NOT_FOUND,
            FILE_NOT_FOUND,
            UNKNOWN_ERROR
        }

        //public int filesInChecksumQueue; //Keep track of how many files are waiting for checksum calculations

        const string applicationName = "Burn Manager", applicationExtension = "chris";

        public MainWindow()
        {
            
            InitializeComponent();
            fileAddTasks = new List<Task>();
            burnQueueTasks = new List<Task>();
            folderAddTasks = new List<Task>();
            
            startBurnPool();

        }

        private void test()
        {
            
        }

         
        private async void openDebugWindow(object sender, RoutedEventArgs e)
        {
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
                //throw new Exception("Debug window failed: debugWindowOpen = true but loop exited without returning.");
            }

            var debugWindow = new DebugOutput();
            debugWindow.Owner = this;
            debugWindow.Topmost = false;
            this.Name = "DebugOutputWindow";
            
            debugWindow.Show();
            debugWindowOpen = true;

            //MessageBox.Show(System.Runtime.InteropServices.Marshal.GetLastWin32Error().ToString(), "Error code", MessageBoxButton.OK, MessageBoxImage.Exclamation);

            //MessageBox.Show(Application.Current.Windows.Count.ToString(), "Windows open", MessageBoxButton.OK,
            //    MessageBoxImage.Exclamation);
            
            while (true)
            {
                debugWindow.DebugOutputTextBox.Text = debugText;
                debugWindow.DebugOutputTaskCounter.Content = "Tasks: " + getPendingTasks();
                await Task.Delay(500);
            }
            
        }

        private async void compareChecksums(object sender, RoutedEventArgs e)
        {
            const string debugName = "MainWindow::compareChecksums():";
            const bool debug = true;
            if (debug) debugEcho(debugName + "Start");
            List<errorCodeAndIndex> badResults = await auditFiles();

            if (badResults.Count == 0)
            {
                debugEcho(debugName + "All files appear normal!");
                return;
            }

            for (int i = 0; i < badResults.Count; i++)
            {
                string outputstring = "Index " + badResults[i].index + " returned " + badResults[i].resultCode.ToString();
                debugEcho(debugName + outputstring);
            }
            if (debug) debugEcho(debugName + "Complete");
        }

        //Audit the files in the burn pool against their file system counterparts, seeing that they still exist and have the same
        //checksums.
        //Also assigns these values to the 'status' property for each file.
        private async Task<List<errorCodeAndIndex>> auditFiles()
        {
            const string debugName = "MainWindow::auditFiles():";
            const bool debug = false;
            debugEcho(debugName + "Starting asynchronous checksum check.");

            List<errorCodeAndIndex> badResults = new List<errorCodeAndIndex>();
            List<Task> taskQueue = new List<Task>();


            await Task.Run(() => {
                //System.Windows.MessageBox.Show("WE HERE");
                for (int i = 0; i < burnpool.allFiles.Count; i++)
                {
                    errorCodeAndIndex asdf;
                    asdf.resultCode = burnpool.compareChecksumToFileSystem(i);
                    asdf.index = i;

                    
                    BurnPoolManager.FileProps replacementFile = burnpool.allFiles[i];
                    replacementFile.fileStatus = asdf.resultCode;
                    burnpool.allFiles[i] = replacementFile;
                    //Yes, this is the only way to alter resultCode; it's a value type and there's no handy way to modify it directly

                    if (asdf.resultCode != BurnPoolManager.errorCode.FILES_EQUAL) badResults.Add(asdf);

                    if (debug) debugEchoAsync(debugName + burnpool.allFiles[i].fileName + ": " + asdf.ToString());
                }
            });

            

            if (debug) debugEcho(debugName + "Done");
            return badResults;

        }

        //Goes to the ListBox in the burn view
        private void ChooseBurnList(object sender, SelectionChangedEventArgs args)
        {
            
        }

        //Called when an item in AllFilesListBox is clicked
        private void ChooseFileList(object sender, SelectionChangedEventArgs args)
        {
            updateMainScreenFileDetails();
        }

        //Updates the file details attributes on the main screen based on whatever file is selected in the file picker.
        private void updateMainScreenFileDetails()
        {
            const string detailsName = "No files selected", detailsPath = "Location: ", detailsSize = "Size: ";
            const string debugName = "updateMainScreenFileDetails():";
            const bool debug = false;
            long fileSizeTally = 0;
            string fileLocation;

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
                AllFiles_DetailsName.Content = burnpool.allFiles[burnpool.findFileByFullPath(AllFilesListBox.SelectedItem.ToString())].fileName;
            }

            //Note: The AllFilesListBox should always display items in the same order as they are in the burnpool.allFiles array
            for (int i = 0; i < AllFilesListBox.SelectedItems.Count; i++)
            {
                try
                {
                    fileSizeTally += burnpool.allFiles[burnpool.findFileByFullPath(AllFilesListBox.SelectedItems[i].ToString())].size;
                    if (i == 0)
                    {
                        AllFiles_DetailsPath.Content = getDirectoryFromPath(AllFilesListBox.SelectedItems[i].ToString());
                    }
                    else
                    {
                        if (getDirectoryFromPath(AllFilesListBox.SelectedItems[i].ToString()) != AllFiles_DetailsPath.Content.ToString())
                        {
                            if (debug)
                            {
                                debugEcho(debugName + "The string [" + getDirectoryFromPath(AllFilesListBox.SelectedItems[i].ToString()) +
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
                    debugEcho(debugOut);
                    System.Windows.MessageBox.Show(debugOut);
                    return;
                }
            }

            AllFiles_DetailsSize.Content = detailsSize + fileSizeTally.ToString() + " bytes";
        }

        //Initializes the currently loaded burnpool and window contents
        private void startBurnPool()
        {
            //string[] directories = { @"C:\Users\coldc\Downloads", @"C:\Users\coldc\Documents\testing data compare\files of various sizes" };

            debugEcho("Initializing burnpool");
            burnpool = new BurnPoolManager(this);
            VolumeSizeTextInput.Text = "";
            updateAllWindows();

            lastSavedInstance = new BurnPoolManager(burnpool);
        }

        private async Task generateBurnListsForPoolAsyncA(long volumeSize)
        {
            debugEcho("generateBurnListsForPoolAsyncA: Start");
            BurnPoolManager.OneBurn aBurn = new BurnPoolManager.OneBurn();

            await Task.Run(() =>
            {
                while (burnpool.generateOneBurn(volumeSize));
                //yes this works; generateOneBurn returns false once it can no longer generate a OneBurn
            });


            //return burnPool;
        }


        //Send any debugging-related text here
        public void debugEcho(string text)
        {
            debugText += text += "\n";
        }

        //For output that isn't strictly debug related & may be useful to users
        public void logOutput(string text)
        {
            debugText += text += "\n";
        }


        public async void debugEchoAsync(string text)
        {
            await Task.Run(() => { debugEcho(text); });
        }

        public void informUser(string text)
        {
            System.Windows.MessageBox.Show(text);
        }

        public void informUser(userMessages message)
        {
            string usertext = "Initialized Value";
            switch (message)
            {
                case userMessages.DISC_ALREADY_BURNED:
                    usertext = "This disc has been burned; files cannot be removed.";
                    break;
                case userMessages.FILE_AUDIT_FAILED:
                    usertext = "File discrepancies were detected, please check the log.";
                    break;
            }
            System.Windows.MessageBox.Show(usertext);
        }

        //Show details for a FileProps in the main list box
        private void FileViewListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            const string debugName = "MainWindow::FileViewListBox_MouseDoubleClick():";
            if (AllFilesListBox.SelectedItems.Count != 1)
            {
                return;
            }

            int? filePropsToGet = burnpool.findFileByFullPath(AllFilesListBox.SelectedItem.ToString());
            if (filePropsToGet == null)
            {
                debugEcho(debugName + "Invalid selection");
                return;
            }

            var fileViewDetails = new FilePropsViewDetails(ref burnpool, this, (int)filePropsToGet);
            fileViewDetails.Owner = this;
            fileViewDetails.Topmost = false;
            //this.Name = "OneBurnViewDetails";

            fileViewDetails.Show();
        }

        //Show details for a OneBurn in the burn view list box
        private void BurnViewListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            const string debugName = "MainWindow::BurnViewListBox_MouseDoubleClick():";
            int? oneBurnToGet = 0;

            if (BurnViewListBox.SelectedItems.Count == 0) return;

            try
            {
                oneBurnToGet = burnpool.getBurnQueueFileByName(BurnViewListBox.SelectedItem.ToString());
            }
            catch (NullReferenceException)
            {
                debugEcho(debugName + "BurnViewListBox.SelectedItem.ToString() Null reference exception");
            }

            if(oneBurnToGet == null)
            {
                debugEcho("BurnViewListBox: Invalid selection");
                return;
            }

            var burnViewDetails = new OneBurnViewDetails(ref burnpool, (int)oneBurnToGet, this);
            burnViewDetails.Owner = this;
            burnViewDetails.Topmost = false;
            //this.Name = "OneBurnViewDetails";

            burnViewDetails.Show();
        }

        private void BurnedDiscsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            int? oneBurnToGet = 0;
            oneBurnToGet = burnpool.getBurnQueueFileByName(BurnedDiscsListBox.SelectedItem.ToString());

            if (oneBurnToGet == null)
            {
                debugEcho("BurnViewListBox: Invalid selection");
                return;
            }

            var burnViewDetails = new OneBurnViewDetails(ref burnpool, (int)oneBurnToGet, this);
            burnViewDetails.Owner = this;
            burnViewDetails.Topmost = false;
            //this.Name = "OneBurnViewDetails";

            burnViewDetails.Show();
        }

        //The button that instructs the program to calculate burn lists based on the files added
        private async void CalculateBurnListButtonClick(object sender, RoutedEventArgs e)
        {
            long volumeSize = 0;
            try
            {
                volumeSize = long.Parse(VolumeSizeTextInput.Text);
            }
            catch (ArgumentNullException)
            {
                debugEcho("CalculateBurnListButtonClick ArgumentNullException: VolumeSizeTextInput.Text == null");
                System.Windows.MessageBox.Show("Invalid volume size, please try again", applicationName);
                return;
            }
            catch (FormatException)
            {
                debugEcho("CalculateBurnListButtonClick FormatException: VolumeSizeTextInput.Text does not appear to be a valid int");
                System.Windows.MessageBox.Show("Invalid volume size, please try again", applicationName);
                return;
            }
            catch (OverflowException)
            {
                debugEcho("CalculateBurnListButtonClick FormatException: Volume size is greater than Int64.MaxValue");
                System.Windows.MessageBox.Show("Volume size is too large. What kind of media are you using??", applicationName);
                return;
            }
            catch (Exception)
            {
                debugEcho("CalculateBurnListButtonClick FormatException: Unknown exception");
                System.Windows.MessageBox.Show("Unknown exception", applicationName);
                return;
            }
            if (getPendingTasks() == 0)
            {
                burnQueueTasks.Add(generateBurnListsForPoolAsyncA(volumeSize));
                updateAllWindowsWhenTasksComplete();
                cleanUpTaskLists();
            }
            else
            {
                operationsInProgressDialog();
            }

        }


        //Button handler to stage a burn
        private void StageThisBurnButtonClick(object sender, RoutedEventArgs e)
        {
            const string debugName = "MainWindow::StageThisBurnButtonClick():";
            int temp = 0;
            errorCode a = StageABurn(0, true);
            debugEcho(debugName + "Result: " + a.ToString());
        }

        //Stage the contents of a OneBurn into the windows burn directory
        //includeBurnRecord = include a text document of this OneBurn's files and checksums
        private errorCode StageABurn(int burnQueueIndex, bool includeBurnRecord)
        {
            const string debugName = "MainWindow::StageABurn():", friendlyName = debugName;
            const bool debug = false;

            string userName = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            userName = userName.Substring(userName.IndexOf("\\") + 1);
            tempBurnFolder = tempBurnFolder.Replace("#UserName", userName);

            if (!Directory.Exists(tempBurnFolder))
            {
                return errorCode.DIRECTORY_NOT_FOUND;
            }

            if (includeBurnRecord)
            {
                if (debug) debugEcho(debugName + "Including burn record");
                string burnRecord = burnpool.burnQueue[burnQueueIndex].ToString();
                FileInfo newFile = new FileInfo(tempBurnFolder + "File Catalog.txt");

                if (debug) debugEcho(debugName + "Space remaining in OneBurn:" + burnpool.burnQueue[burnQueueIndex].spaceRemaining +
                    "\nSpace occupied by file output:" + System.Text.ASCIIEncoding.Unicode.GetByteCount(burnRecord));

                while (System.Text.ASCIIEncoding.Unicode.GetByteCount(burnRecord) > burnpool.burnQueue[burnQueueIndex].spaceRemaining)
                {
                    if (debug) debugEcho(debugName + "Removing file " + burnpool.burnQueue[burnQueueIndex].files[burnpool.burnQueue[burnQueueIndex].files.Count - 1].fileName +
                        " with the size " + burnpool.burnQueue[burnQueueIndex].files[burnpool.burnQueue[burnQueueIndex].files.Count - 1].size +
                        " to make space for the output of size " + System.Text.ASCIIEncoding.Unicode.GetByteCount(burnRecord));


                    burnpool.removeFileFromOneBurn(burnQueueIndex, burnpool.burnQueue[burnQueueIndex].files.Count - 1);
                    burnRecord = burnpool.burnQueue[burnQueueIndex].ToString();

                }

                using (StreamWriter streamtime = newFile.CreateText())
                {
                    streamtime.WriteLine(burnRecord);
                }
            }

            List<FileInfo> filesToCopy = new List<FileInfo>();
            bool foundErrors = false;
            for (int i = 0; i < burnpool.burnQueue[burnQueueIndex].files.Count; i++)
            {
                FileInfo newFile = new FileInfo(burnpool.burnQueue[burnQueueIndex].files[i].originalPath);
                if (!newFile.Exists)
                {
                    burnpool.setErrorCode(burnpool.findFileByFullPath(burnpool.burnQueue[burnQueueIndex].files[i].originalPath), BurnPoolManager.errorCode.FILE_NOT_FOUND_IN_FILESYSTEM);
                    foundErrors = true;
                }
                else
                {
                    filesToCopy.Add(newFile);
                }
            }

            if (foundErrors)
            {
                return errorCode.FILE_NOT_FOUND;
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
                    catch(Exception e)
                    {
                        informUser(friendlyName + "Exception \"" + e + "\" was thrown when copying " + filesToCopy[i].FullName + 
                            " to the temporary burn path at " + tempBurnFolder);
                        return errorCode.UNKNOWN_ERROR;
                    }
                }
            }

            
            return errorCode.SUCCESS;
        }


        //Called when the user instructs the program to mark a OneBurn as burned to disc
        private void MarkBurnedButtonClick(object sender, RoutedEventArgs e)
        {
            const string debugName = "MainWindow::MarkBurnedButtonClick():";

            if (BurnViewListBox.SelectedItems.Count == 0)
            {
                string debugtext = "Please select a burn to mark as burned.";
                System.Windows.MessageBox.Show(debugtext);
                return;
            }

            /*
            MessageBoxResult result = System.Windows.MessageBox.Show("Are you sure you want to remove the burn list \"" +
                BurnViewListBox.SelectedItem.ToString() + "\"?",
                    applicationName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                return;
            }
            */

            int? oneBurnToCommit = 0;

            try
            {
                oneBurnToCommit = burnpool.getBurnQueueFileByName(BurnViewListBox.SelectedItem.ToString());
            }
            catch (NullReferenceException)
            {
                debugEcho(debugName + "Null reference thrown when attempting burnpool.getBurnQueueFileByName. Was a OneBurn" +
                    " selected in the ListView?");
                return;
            }

            if (oneBurnToCommit == null)
            {
                debugEcho(debugName + "The OneBurn \"" + BurnViewListBox.SelectedItem.ToString() + "\" was not found in burnQueue.");
                return;
            }

            BurnPoolManager.errorCode result = burnpool.commitOneBurn((int)oneBurnToCommit, true);

            if (result != BurnPoolManager.errorCode.SUCCESS)
            {
                informUser(userMessages.FILE_AUDIT_FAILED);
            }

            updateAllWindows();
        }

        private void MarkUnburnedButtonClick(object sender, RoutedEventArgs e)
        {
            const string debugName = "MainWindow::MarkUnburnedButtonClick():";

            if (BurnedDiscsListBox.SelectedItems.Count == 0)
            {
                string debugtext = "Please select a burn to unmark as burned.";
                System.Windows.MessageBox.Show(debugtext);
                return;
            }

            /*
            MessageBoxResult result = System.Windows.MessageBox.Show("Are you sure you want to remove the burn list \"" +
                BurnViewListBox.SelectedItem.ToString() + "\"?",
                    applicationName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                return;
            }
            */

            int? oneBurnToUncommit = 0;

            try
            {
                oneBurnToUncommit = burnpool.getBurnQueueFileByName(BurnedDiscsListBox.SelectedItem.ToString());
            }
            catch (NullReferenceException)
            {
                debugEcho(debugName + "Null reference thrown when attempting burnpool.getBurnQueueFileByName. Was a OneBurn" +
                    " selected in the ListView?");
                return;
            }

            if (oneBurnToUncommit == null)
            {
                debugEcho(debugName + "The OneBurn \"" + BurnedDiscsListBox.SelectedItem.ToString() + "\" was not found in burnQueue.");
                return;
            }

            burnpool.uncommitOneBurn((int)oneBurnToUncommit);

            updateAllWindows();
        }

        private void RemoveBurnButtonClick(object sender, RoutedEventArgs e)
        {
            const string debugName = "MainWindow::RemoveBurnButtonClick:";
            if (BurnViewListBox.SelectedItems.Count == 0)
            {
                string debugtext = "Please select a burn to remove.";
                System.Windows.MessageBox.Show(debugtext);
                return;
            }

            MessageBoxResult result = System.Windows.MessageBox.Show("Are you sure you want to remove the burn list \"" +
                BurnViewListBox.SelectedItem.ToString() + "\"?",
                    applicationName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                return;
            }

            try
            {
                burnpool.deleteOneBurn((int)burnpool.getBurnQueueFileByName(BurnViewListBox.SelectedItem.ToString()));
            }
            catch (NullReferenceException)
            {
                debugEcho(debugName + "Null reference exception: Null returned when attempting to find OneBurn titled \"" +
                    BurnViewListBox.SelectedItem.ToString() + "\"");
            }

            updateAllWindows();
        }

        private void VolumeSizeTextInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string hold = VolumeSizeTextInput.Text;
            
        }

        //Opens the file picker and adds any files chosen to the burn pool.
        //At the moment this function does not check for outstanding fileAddTasks as it seems to work fine adding more files to
        //the list while operations are pending. However, it does check for burnQueueTasks
        private async void OpenFilePicker(object sender, RoutedEventArgs e)
        {
            const bool debugVerbose = true, msgBoxes = false;
            const string debugName = "OpenFilePicker:";
            FileOpenPicker picker = new FileOpenPicker();
            //List < Task > tasks = new List<Task>();
            picker.ViewMode = PickerViewMode.List;
            //picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");
            
            

            if (getPendingTasks() > 0)
            {
                System.Windows.MessageBox.Show("Some operations are still in progress. Please wait for operations " +
                    "to finish before adding more files.");
                return;
            }

            //Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();

            var win32moment = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr handle = win32moment.Handle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
            IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();

            if (msgBoxes) System.Windows.MessageBox.Show("File picker happened");


            if (files.Count == 0)
            {
                return;
            }


            if (debugVerbose)
            {
                //MessageBox.Show("IReadOnlyList<StorageFile> files is now populated");
                debugEcho(debugName + "IReadOnlyList<StorageFile> files is now populated");
            }

            

            await Task.Run(() => {
                //int filesPrior = burnpool.allFiles.Count;
                List<BurnPoolManager.FileProps> newFileBuffer = new List<BurnPoolManager.FileProps>();
                if (msgBoxes) System.Windows.MessageBox.Show("task START");
                foreach (StorageFile file in files)
                {
                    //burnpool.addFile(file);
                    try
                    {
                        fileAddTasks.Add(burnpool.addFileAsync(file));
                    }
                    catch
                    {
                        debugEcho(debugName + "Exception while adding files to the burnpool.");
                        System.Windows.MessageBox.Show(debugName + "Exception while adding files to the burnpool.", applicationName);
                    }
                }
            
                /*
                int tasksInProgress = 0, lastTasksInProgress = 0;
                do
                {
                    tasksInProgress = 0;
                    //debugEcho("DO!!!");
                    if (msgBoxes) System.Windows.MessageBox.Show("WE ARE DOING!");
                    for (int i = 0; i < fileAddTasks.Count; i++)
                    {
                        if (!fileAddTasks[i].IsCompleted)
                        {
                            tasksInProgress++;
                        }
                    }
                    if (tasksInProgress != lastTasksInProgress && debugVerbose)
                    {
                        debugEcho(debugName + "Tasks in progress: " + tasksInProgress);
                        lastTasksInProgress = tasksInProgress;
                    }
                    //StatusField.Content = (debugName + "Tasks in progress: " + tasksInProgress);

                }
                while (tasksInProgress > 0 || false);
                */
            });

            if (debugVerbose)
            {
                //MessageBox.Show("All files have been added to burnpool");
                debugEcho(debugName + "All files have been added to burnpool");
            }

            updateAllWindowsWhenTasksComplete();
            cleanUpTaskLists();


        }

        private async void OpenFolderPicker(object sender, RoutedEventArgs e)
        {
            const bool debugVerbose = true;
            const string debugName = "OpenFolderPicker:";
            FolderPicker picker = new FolderPicker();
            //List < Task > tasks = new List<Task>();
            picker.ViewMode = PickerViewMode.List;
            //picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");



            if (getPendingTasks() > 0)
            {
                System.Windows.MessageBox.Show("Some operations are still in progress. Please wait for operations " +
                    "to finish before adding more files.");
                return;
            }

            //Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();

            var win32moment = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr handle = win32moment.Handle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
            Windows.Storage.StorageFolder theFolder = await picker.PickSingleFolderAsync();

            if (theFolder == null)
            {
                debugEcho(debugName + "theFolder = null, this is normal if the user clicked cancel.");
                return;
            }

            if (debugVerbose) debugEcho(debugName + "Adding folder " + theFolder.Name);
            IReadOnlyList<IStorageItem> foldercontents = await theFolder.GetItemsAsync();
            folderAddTasks.Add(addFolderRecursive(foldercontents, true));

            //System.Windows.MessageBox.Show(fileAddTasks.Count.ToString());

            await Task.Run(() => {
                if (debugVerbose) debugEcho(debugName + "Starting folder task check loop");
                
                /*
                int tasksInProgress = 0, lastTasksInProgress = 0;
                bool nullcatch = false;
                
                do
                {
                    tasksInProgress = 0;
                    nullcatch = false;
                    if (fileAddTasks == null)
                    {
                        //The number of tasks in progress can vary while this function runs, and can even be null if this
                        //block is reached before addFolderRecursive returns. This ensures it doesn't begin counting until at
                        //least one task has been added to the task list.
                        //This could present a future problem if someone has a really fast computer, so hopefully that doesn't happen
                        //debugEcho("Woppa!!");
                        nullcatch = true;
                        break;
                    }
                    for (int i = 0; i < fileAddTasks.Count; i++)
                    {
                        
                        if (fileAddTasks == null || fileAddTasks[i] == null || i > fileAddTasks.Count)
                        {
                            //The number of tasks in progress can vary while this function runs, and can even be null if this
                            //block is reached before addFolderRecursive returns. This ensures it doesn't begin counting until at
                            //least one task has been added to the task list.
                            //This could present a future problem if someone has a really fast computer, so hopefully that doesn't happen
                            //debugEcho("Woppa!!");
                            nullcatch = true;
                            break;
                        }
                        
                        if (!fileAddTasks[i].IsCompleted)
                        {
                            tasksInProgress++;
                        }
                    }
                    if (tasksInProgress != lastTasksInProgress && debugVerbose)
                    {
                        debugEcho(debugName + "Tasks in progress: " + tasksInProgress);
                        lastTasksInProgress = tasksInProgress;
                    }
                    //StatusField.Content = (debugName + "Tasks in progress: " + tasksInProgress);

                }
                while (tasksInProgress > 0 || nullcatch == true);
                */

                if (debugVerbose) debugEcho(debugName + "Ending folder task check loop");
            });

            updateAllWindowsWhenTasksComplete();
            cleanUpTaskLists();

            if (debugVerbose)
            {
                //MessageBox.Show("All files have been added to burnpool");
                debugEcho(debugName + "All files have been added to burnpool");
            }

            //updateAllWindows();


        }

        private async Task<Task> addFolderRecursive(IReadOnlyList<IStorageItem> foldercontents, bool recursion)
        {
            const string debugName = "MainWindow::addFolderRecursive():", friendlyName = debugName;
            const bool debugVerbose = true;
            
            //IReadOnlyList<IStorageItem> foldercontents = await theFolder.GetItemsAsync();

            List<StorageFile> files = new List<StorageFile>();
            List<StorageFolder> folders = new List<StorageFolder>();


            await Task.Run(() => { 
                foreach (IStorageItem item in foldercontents)
                {
                    //if (debugVerbose) debugEcho("BLORF");
                    if (item.IsOfType(StorageItemTypes.File))
                    {
                        files.Add((StorageFile)item);
                    }
                    if (item.IsOfType(StorageItemTypes.Folder) && recursion)
                    {
                        //while (getPendingTasks() > taskLimit) await Task.Delay(1000);
                        startNewFolderTask((StorageFolder)item);
                    }
                }
            });

            //IReadOnlyList<StorageFile> files = (IReadOnlyList<StorageFile>)await theFolder.GetItemsAsync();

            if (debugVerbose)
            {
                //MessageBox.Show("IReadOnlyList<StorageFile> files is now populated");
                debugEcho(debugName + "IReadOnlyList<StorageFile> files is now populated");
            }



            return Task.Run(() => {
                //int filesPrior = burnpool.allFiles.Count;
                //List<BurnPoolManager.FileProps> newFileBuffer = new List<BurnPoolManager.FileProps>();

                foreach (StorageFile file in files)
                {
                    //burnpool.addFile(file);
                    try
                    {
                        fileAddTasks.Add(burnpool.addFileAsync(file));
                    }
                    catch
                    {
                        debugEcho(debugName + "Exception while adding files to the burnpool.");
                        System.Windows.MessageBox.Show(debugName + "Exception while adding files to the burnpool.", applicationName);
                    }
                }
                //updateAllWindowsWhenTasksComplete();
                //cleanUpTaskLists();
            });

            if (debugVerbose) debugEcho(debugName + "Tasks completed");
        }

        private async void startNewFolderTask(StorageFolder folder)
        {
            
            //while (getPendingTasks() > tasklimit) await Task.Delay(1000);
            IReadOnlyList<IStorageItem> newfolder = await folder.GetItemsAsync();
            fileAddTasks.Add(addFolderRecursive(newfolder, true));
        }

        //Handler for the button that removes files
        private void AllFiles_RemoveFileButtonClick(object sender, RoutedEventArgs e)
        {
            if (AllFilesListBox.SelectedItems.Count == 0)
            {
                System.Windows.MessageBox.Show("Please select at least one file to remove.");
                return;
            }
            for (int i = 0; i < AllFilesListBox.SelectedItems.Count; i++)
            {
                burnpool.removeFile(AllFilesListBox.SelectedItems[i].ToString());
            }
            updateAllWindows();
        }

        //Update the main file view
        private void updateMainScreenFileView()
        {
            //MainWindow_FileView.Text = burnpool.burnPoolToString();
            const bool debug = true;
            const string debugName = "updateMainScreenFileView():";
            AllFilesListBox.Items.Clear();
            for (int i = 0; i < burnpool.allFiles.Count; i++)
            {
                //AllFilesListBox.Items.Add(burnpool.allFiles[i].fileName);
                AllFilesListBox.Items.Add(burnpool.allFiles[i].originalPath);
            }


            //Check whether there is a file with an identical filename and append an asterisk
            //Important as identical filenames could cause issues with getting file attributes by clicking on the list
            /*
            if (debug) debugEcho(debugName + "Starting identical filename check");
            for (int i = 0; i < AllFilesListBox.Items.Count; i++)
            {
                for (int x = i + 1; x < AllFilesListBox.Items.Count; x++)
                {
                    if (AllFilesListBox.Items[i].ToString() == AllFilesListBox.Items[x].ToString() )
                    {
                        //System.Windows.MessageBox.Show(AllFilesListBox.Items[i].ToString() + " is identical to " +
                            //AllFilesListBox.Items[x].ToString());
                        AllFilesListBox.Items[x] += "*";
                    }
                }
            }
            if (debug) debugEcho(debugName + "Completed identical filename check");
            */

        }

        //Populate the burn window with whatever is in burnpool.burnQueue. Always blanks the burn window first.
        private void populateBurnWindow()
        {
            BurnViewListBox.Items.Clear();
            for (int i = 0; i < burnpool.burnQueue.Count; i++)
            {
                if (burnpool.burnQueue[i].timesBurned == 0)
                {
                    BurnViewListBox.Items.Add(burnpool.burnQueue[i].name);
                }
            }
        }

        private void populateBurnedWindow()
        {
            BurnedDiscsListBox.Items.Clear();
            for (int i = 0; i < burnpool.burnQueue.Count; i++)
            {
                if (burnpool.burnQueue[i].timesBurned > 0)
                {
                    BurnedDiscsListBox.Items.Add(burnpool.burnQueue[i].name);
                }
            }
        }

        //Update the data display in all windows
        private void updateAllWindows()
        {
            updateMainScreenFileView();
            populateBurnWindow();
            populateBurnedWindow();
        }

        private async void updateAllWindowsWhenTasksComplete()
        {
            const bool debug = true, msgBoxes = false;
            const string debugName = "MainWindow::updateAllWindowsWhenTasksComplete():";
            if (debug) debugEcho(debugName + "Start");
            if (msgBoxes) System.Windows.MessageBox.Show(debugName + "Start");
            await Task.Run(() =>
            {
                while (getPendingTasks() > 0) ;
            });
            if (debug) debugEcho(debugName + "Updating windows now.");
            if (msgBoxes) System.Windows.MessageBox.Show(debugName + "End");
            updateAllWindows();
        }

        //Null catches are in case the task list changes in size when this is running
        private int getPendingTasks()
        {
            int tasks = 0;
            for (int i = 0; i < fileAddTasks.Count; i++)
            {
                try
                {
                    if (fileAddTasks[i] == null) break;
                    if (!fileAddTasks[i].IsCompleted) tasks++;
                }
                catch (NullReferenceException)
                {
                    break;
                }
            }
            for (int i = 0; i < burnQueueTasks.Count; i++)
            {
                try
                {
                    if (burnQueueTasks[i] == null) break;
                    if (!burnQueueTasks[i].IsCompleted) tasks++;
                }
                catch (NullReferenceException)
                {
                    break;
                }
            }
            for (int i = 0; i < folderAddTasks.Count; i++)
            {
                try
                {
                    if (folderAddTasks[i] == null) break;
                    if (!folderAddTasks[i].IsCompleted) tasks++;
                }
                catch
                {
                    break;
                }
            }
            return tasks;
        }

        private int getBurnQueueTasks()
        {
            int tasks = 0;
            for (int i = 0; i < burnQueueTasks.Count; i++)
            {
                if (!burnQueueTasks[i].IsCompleted) tasks++;
            }
            return tasks;
        }

        //Removes all completed tasks from fileAddTasks and burnQueueTasks
        private async void cleanUpTaskLists()
        {
            const string debugName = "MainWindow::cleanUpTaskLists():";
            const bool debug = true, msgBoxes = true;

            if (debug) debugEcho(debugName + "Start");

            do
            {
                await Task.Delay(10000);
            }
            while (getPendingTasks() > 0);

            //The task lists can change in size while this function is running when recursive folder adds are running, so a lot
            //of seemingly arbitrary checks are utilized.

            await Task.Run(() => { 
                bool rangeCheck = false;
                do
                {
                    rangeCheck = false;
                    for (int i = 0; i < fileAddTasks.Count; i++)
                    {
                        try
                        {
                            if (i < fileAddTasks.Count && fileAddTasks[i].IsCompleted)
                            {
                                fileAddTasks.RemoveAt(i);
                                i = -1;
                            }
                            else if (i > fileAddTasks.Count)
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
                    for (int i = 0; i < burnQueueTasks.Count; i++)
                    {
                        try
                        {
                            if (i < burnQueueTasks.Count && burnQueueTasks[i].IsCompleted)
                            {
                                burnQueueTasks.RemoveAt(i);
                                i = -1;
                            }
                            else if (i > burnQueueTasks.Count)
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
                }
                while ((getPendingTasks() == 0 && fileAddTasks.Count > 0 || burnQueueTasks.Count > 0) || rangeCheck == true);
            });

            if (debug) debugEcho(debugName + "Finish");
        }

        //Pass a full path to a file and get back just the directory it's in
        private string getDirectoryFromPath(string path)
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
                debugEcho("getDirectoryFromPath: Path " + path + " appears to be invalid.");
                return "";
            }
            return (path.Substring(0, lastSlash + 1));
        }

        //Detect whether changes have been made since the file was last saved, or since a new file was created
        private bool changesMade()
        {
            //debugEcho("changesMade burnpool:" + burnpool.burnPoolToString());
            //debugEcho("changesmade lastSavedInstance:" + lastSavedInstance.burnPoolToString());
            if (burnpool == lastSavedInstance) return false;
            return true;
        }

        private void saveFileDialog()
        {
            const string debugName = "saveFileDialog:";
            string serialized = "";
            try
            {
                serialized = JsonSerializer.Serialize(burnpool);
            }
            catch
            {
                debugEcho("FileSave_Click: Exception thrown using JsonSerializer.Serialize()");
                System.Windows.MessageBox.Show("FileSave_Click: Exception thrown using JsonSerializer.Serialize()");
                return;
            }

            if (serialized == null)
            {
                debugEcho(debugName + "serialized == null");
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
                        lastSavedInstance = new BurnPoolManager(burnpool);
                    }
                    else
                    {
                        debugEcho(debugName + "saveDialog.OpenFile() did not != null");
                    }
                }
                else
                {
                    debugEcho(debugName + "saveDialog.ShowDialog() did not == System.Windows.Forms.DialogResult.OK");
                }
            }
            catch
            {
                debugEcho(debugName + "Unknown exception");
            }
        }

        private void operationsInProgressDialog()
        {
            System.Windows.MessageBox.Show("Some operations are still in progress. Please wait until operations complete.");
        }

                    //Top menu bar items start below


        private void FileSave_Click(object sender, RoutedEventArgs e)
        {
            const string debugName = "FileSave_Click:";
            if (getPendingTasks() > 0)
            {
                operationsInProgressDialog();
                return;
            }
            saveFileDialog();
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            const string debugName = "New_Click():";

            if (getPendingTasks() > 0)
            {
                operationsInProgressDialog();
                return;
            }


            if (changesMade())
            {
                MessageBoxResult result = System.Windows.MessageBox.Show("Changes have been made. Do you want to save?",
                    applicationName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    saveFileDialog();
                }
                if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
            }

            debugEcho("New file start");
            startBurnPool();
        }

        

        private void MixedUseButton(object sender, RoutedEventArgs e)
        {
            //debugEcho(burnpool.burnQueue[0].ToString());
            updateAllWindowsWhenTasksComplete();
            cleanUpTaskLists();
        }

        private async void FileOpen_Click(object sender, RoutedEventArgs e)
        {
            const bool debugVerbose = false;
            const string debugName = "FileLoad_Click:";

            if (getPendingTasks() > 0)
            {
                operationsInProgressDialog();
                return;
            }

            if (changesMade())
            {
                MessageBoxResult result = System.Windows.MessageBox.Show("Changes have been made. Do you want to save?", 
                    applicationName, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    saveFileDialog();
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
                    debugEcho(debugName + "FileOpenPicker result is null. This is expected behavior if the user clicked cancel.");
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
                        debugEcho(debugName + "Null returned from await Windows.Storage.FileIO.ReadTextAsync");
                        return;
                    }

                    burnpool = new BurnPoolManager(this);

                    try
                    {
                        burnpool = JsonSerializer.Deserialize<BurnPoolManager>(serializedJson);
                    }
                    catch (ArgumentNullException)
                    {
                        const string errortext = debugName + "JsonSerializer.Deserialize<BurnPoolManager>(serializedJson): serializedJson = null";
                        debugEcho(errortext);
                        System.Windows.MessageBox.Show(errortext);
                        return;
                    }
                    catch (JsonException)
                    {
                        const string errortext = debugName + "JsonSerializer.Deserialize<BurnPoolManager>(serializedJson): JsonException." +
                            " File may be formatted incorrectly or corrupt.";
                        debugEcho(errortext);
                        System.Windows.MessageBox.Show(errortext);
                        return;
                    }
                    catch
                    {
                        const string errortext = debugName + "JsonSerializer.Deserialize<BurnPoolManager>(serializedJson): Unknown exception.";
                        debugEcho(errortext);
                        System.Windows.MessageBox.Show(errortext);
                        return;
                    }
                    burnpool.mainWindow = this;
                    lastSavedInstance = new BurnPoolManager(burnpool);
                    updateAllWindows();
                }
                catch
                {
                    debugEcho(debugName + "Exception thrown when deserializing");
                }
            }
            catch
            {
                debugEcho(debugName + "Exception thrown when initializing FileOpenPicker");
                return;
            }


        }


    }
}
