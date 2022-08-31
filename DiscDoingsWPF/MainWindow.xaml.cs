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
        string debugText = "Debug Output\nThis version has locks in place for additions to allFiles."; //Append any debug related messages to this string
        const string applicationName = "Burn Manager", applicationExtension = "chris";
        bool debugWindowOpen = false;
        List<Task> fileAddTasks, burnQueueTasks, folderAddTasks, auditTasks;

        //don't const this, it will be useful to let the user change it later
        string tempBurnFolder = "C:\\Users\\#UserName\\AppData\\Local\\Microsoft\\Windows\\Burn\\Burn1\\";

        public BurnPoolManager burnpool;
        private BurnPoolManager lastSavedInstance; //Use to keep track of whether changes have been made

        //This is used by the informUser() function, it's to make those function calls a little more readable
        public enum userMessages
        {
            DISC_ALREADY_BURNED,
            FILE_AUDIT_FAILED,
            TASKS_PENDING
        }

        public enum errorCode
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
            fileAddTasks = new List<Task>();
            burnQueueTasks = new List<Task>();
            folderAddTasks = new List<Task>();
            auditTasks = new List<Task>();
            startBurnPool();
            updateAllWindowsWhenTasksReachZero();

            
        }

        private void test()
        {
            
        }

         
        private async void openDebugWindow(object sender, RoutedEventArgs e)
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
                debugWindow.DebugOutputTextBox.Text = debugText;
                debugWindow.DebugOutputTaskCounter.Content = "Tasks: " + getPendingTasks();
                await Task.Delay(500);
            }
            
        }

        private async void compareChecksums(object sender, RoutedEventArgs e)
        {
            const string debugName = "MainWindow::compareChecksums():";
            const bool debug = false;
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
            object lockObject = new object();

            List<errorCodeAndIndex> badResults = new List<errorCodeAndIndex>();
            //List<Task> taskQueue = new List<Task>();

            if (false) //Asynchronous version (currently broken)
            {
                for (int i = 0; i < burnpool.allFiles.Count; i++)
                {
                    auditTasks.Add(Task.Run(() =>
                    {
                        errorCodeAndIndex temp;
                        temp.resultCode = burnpool.compareChecksumToFileSystem(i);
                        temp.index = i;
                        if (temp.resultCode != BurnPoolManager.errorCode.FILES_EQUAL)
                        {
                            lock (lockObject)
                            {
                                badResults.Add(temp);
                                burnpool.setErrorCode(i, temp.resultCode);
                            }
                        }
                    }));
                }
            }

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
            const string hello = "Welcome to " + applicationName + "!\nYou are currently experiencing build 0.1, the first proof of concept build.\nAll core functionality is present, but it's not very pretty yet.\nPlease test it out and feel free to inform me of any bugs, or questionable behavior.\nView the Log to see this message again and usage instructions. Have fun!";
            const string instructions = "\n\nUsage instructions:\nStart by adding any files you would like in your backup using \"Add Files!\" or \"Add a Directory!\"\nAn MD5 checksum will be made of each file as it is added. To recheck these checksums, you can click the \"Audit All Files!\" button. Keep in mind that these operations may be slow when processing many files.\nDouble-click a file in the 'All files' list to see more details about it.\"" +
                "\nThe 'Burn view' tab is where it will divide the files out for you. Put in the size of your backup media in bytes and click 'Generate individual burns.' \nOnce it's done, your files will have been distributed to fit efficiently across however many volumes are needed. Hopefully, this will be as few volumes as possible (further algorithm tweaks may come in the future!)\nThe 'Stage this burn' button will stage the burn into your Windows temporary burn directory, and include a text file listing every file that is going into that burn plus some attributes.\nIf there isn't room for this text file, the smallest file(s) will be removed from that burn to make space.\nOnce your volume is burned, cilck 'Mark this as burned' to move it to the discs burned tab. \nEach file will have its hash rechecked against the file system when you do this to ensure integrity!\nOnce a disc is marked as burned, files belonging to that burn can't be removed from the main file list, and that burn cannot be deleted, unless it's unmarked.";
            debugEcho("Initializing burnpool");
            burnpool = new BurnPoolManager(this);
            VolumeSizeTextInput.Text = "";
            updateAllWindows();

            debugEcho("Using file version " + BurnPoolManager.formatVersion);
            debugEcho(hello);
            informUser(hello);
            debugEcho(instructions);

            lastSavedInstance = new BurnPoolManager(burnpool);
        }

        private async Task generateBurnListsForPoolAsyncA(long volumeSize)
        {
            const bool debug = false;
            if (debug) debugEcho("generateBurnListsForPoolAsyncA: Start");
            BurnPoolManager.OneBurn aBurn = new BurnPoolManager.OneBurn();

            await Task.Run(() =>
            {
                while (burnpool.generateOneBurn(volumeSize));
                //yes this works; generateOneBurn returns false once it can no longer generate a OneBurn
            });
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
                case userMessages.TASKS_PENDING:
                    usertext = "There are still tasks in progress. Please wait until tasks finish.";
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
                oneBurnToStage = burnpool.getBurnQueueFileByName(BurnViewListBox.SelectedItem.ToString());
            }
            catch (NullReferenceException)
            {
                debugEcho(debugName + "Null reference thrown when attempting burnpool.getBurnQueueFileByName. Was a OneBurn" +
                    " selected in the ListView?");
                return;
            }

            if (oneBurnToStage == null)
            {
                debugEcho(debugName + "The OneBurn \"" + BurnViewListBox.SelectedItem.ToString() + "\" was not found in burnQueue.");
                return;
            }


            errorCode a = StageABurn((int)oneBurnToStage, true);
            logOutput(friendlyName + "The burn " + burnpool.burnQueue[(int)oneBurnToStage].name + " was staged with the result code " + a.ToString() + ".");
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
                    catch (IOException e)
                    {
                        logOutput(friendlyName + "IO Exception was thrown when copying \"" + filesToCopy[i].FullName +
                            "\". Sometimes copy discrepancies can occur when copying from a network volume. If a \"File Already Exists\" exception is thrown when the directory was initially empty, this can be regarded. Full exception text: \n"
                            + e);
                    
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

            if (getPendingTasks() > 0)
            {
                informUser(userMessages.TASKS_PENDING);
            }

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

            updateAllWindowsWhenTasksComplete();
        }

        private void MarkUnburnedButtonClick(object sender, RoutedEventArgs e)
        {
            const string debugName = "MainWindow::MarkUnburnedButtonClick():";

            if (BurnedDiscsListBox.SelectedItems.Count == 0)
            {
                string debugtext = "Please select a burn to unmark as burned.";
                informUser(debugtext);
                return;
            }


            MessageBoxResult result = System.Windows.MessageBox.Show("Are you sure you want to mark the burn list \"" +
                BurnedDiscsListBox.SelectedItem.ToString() + "\" as unburned?",
                    applicationName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                return;
            }

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
                informUser(debugtext);
                return;
            }

            MessageBoxResult result = System.Windows.MessageBox.Show("Are you sure you want to remove the burn list \"" +
                BurnViewListBox.SelectedItem.ToString() + "\"?",
                    applicationName, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.No)
            {
                return;
            }

            if (getPendingTasks() > 0)
            {
                informUser(userMessages.TASKS_PENDING);
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

        /*
        private void VolumeSizeTextInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string hold = VolumeSizeTextInput.Text;
            
        }
        */

        //Opens the file picker and adds any files chosen to the burn pool.
        //At the moment this function does not check for outstanding fileAddTasks as it seems to work fine adding more files to
        //the list while operations are pending. However, it does check for burnQueueTasks
        private async void OpenFilePicker(object sender, RoutedEventArgs e)
        {
            const bool debugVerbose = false, msgBoxes = false, logging = true;
            const string debugName = "OpenFilePicker:", friendlyName = "";
            FileOpenPicker picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.List;
            //picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");
            
            
            
            if (getBurnQueueTasks() > 0)
            {
                informUser("Some operations are still in progress. Please wait for operations " +
                    "to finish before adding more files.");
                return;
            }
            

            var win32moment = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr handle = win32moment.Handle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
            IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
            int filesSelected = files.Count;

            if (logging) logOutput(friendlyName + "Preparing to add " + filesSelected + " files.");
            if (msgBoxes) System.Windows.MessageBox.Show("File picker happened");


            if (files.Count == 0)
            {
                return;
            }


            if (debugVerbose)
            {
                debugEcho(debugName + "IReadOnlyList<StorageFile> files is now populated");
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
                        fileAddTasks.Add(burnpool.addFileAsync(file));
                    }
                    catch
                    {
                        logOutput(debugName + "Exception while adding files to the burnpool.");
                        //System.Windows.MessageBox.Show(debugName + "Exception while adding files to the burnpool.", applicationName);
                        filesSelected--;
                        issues = true;
                    }
                }
            });

            if (debugVerbose)
            {
                debugEcho(debugName + "All files have been added to burnpool");
            }

            updateAllWindowsWhenTasksComplete();
            cleanUpTaskLists();

            if (logging)
            {
                if (!issues)
                {
                    logOutput(friendlyName + "Successfully added " + filesSelected + " files to the queue.");
                }
                else
                {
                    logOutput(friendlyName + "Successfully added " + filesSelected + " files to the queue. There were some errors, so not all files were added.");
                }
                
            }
            

        }

        private async void OpenFolderPicker(object sender, RoutedEventArgs e)
        {
            const bool debugVerbose = false, logging = true;
            const string debugName = "OpenFolderPicker:", friendlyName = "";
            FolderPicker picker = new FolderPicker();
            picker.ViewMode = PickerViewMode.List;
            //picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");



            if (getBurnQueueTasks() > 0)
            {
                //informUser("Some operations are still in progress. Please wait for operations " +
                //    "to finish before adding more files.");
                informUser(userMessages.TASKS_PENDING);
                return;
            }



            var win32moment = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr handle = win32moment.Handle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
            Windows.Storage.StorageFolder theFolder = await picker.PickSingleFolderAsync();

            if (theFolder == null)
            {
                if (debugVerbose) debugEcho(debugName + "theFolder = null, this is normal if the user clicked cancel.");
                return;
            }

            if (debugVerbose) debugEcho(debugName + "Adding folder " + theFolder.Name);
            if (logging) logOutput(friendlyName + "Adding folder " + theFolder.Name);
            IReadOnlyList<IStorageItem> foldercontents = await theFolder.GetItemsAsync();
            folderAddTasks.Add(addFolderRecursive(foldercontents, true));

            
            updateAllWindowsWhenTasksComplete();
            cleanUpTaskLists();

            if (debugVerbose)
            {
                debugEcho(debugName + "All files have been added to burnpool");
            }


        }

        private async Task<Task> addFolderRecursive(IReadOnlyList<IStorageItem> foldercontents, bool recursion)
        {
            const string debugName = "MainWindow::addFolderRecursive():", friendlyName = "";
            const bool debugVerbose = false, logging = true;
            
            List<StorageFile> files = new List<StorageFile>();
            List<StorageFolder> folders = new List<StorageFolder>();


            await Task.Run(() => { 
                foreach (IStorageItem item in foldercontents)
                {
                    if (item.IsOfType(StorageItemTypes.File))
                    {
                        files.Add((StorageFile)item);
                    }
                    if (item.IsOfType(StorageItemTypes.Folder) && recursion)
                    {
                        if (logging) logOutput(friendlyName + "Adding the folder \"" + item.Name + "\" to the task queue.");
                        startNewFolderTask((StorageFolder)item);
                    }
                }
            });

            if (debugVerbose)
            {
                debugEcho(debugName + "IReadOnlyList<StorageFile> files is now populated");
            }
            if (logging)
            {
                logOutput(friendlyName + "Adding " + files.Count + " files to the task queue.");
            }



            return Task.Run(() => {

                foreach (StorageFile file in files)
                {
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
                if (files.Count > 0) {
                    logOutput(friendlyName + "Completed adding the folder \"" + files[0].Path.Substring(0, files[0].Path.LastIndexOf("\\")) + "\" to the task queue.");
                }
            });

            if (debugVerbose) debugEcho(debugName + "Tasks completed");
        }

        private async void startNewFolderTask(StorageFolder folder)
        {
            bool logging = true;
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
            updateAllWindowsWhenTasksComplete();
        }

        //Update the main file view
        private void updateMainScreenFileView()
        {
            const bool debug = true;
            const string debugName = "updateMainScreenFileView():";
            AllFilesListBox.Items.Clear();
            for (int i = 0; i < burnpool.allFiles.Count; i++)
            {
                AllFilesListBox.Items.Add(burnpool.allFiles[i].originalPath);
            }


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
            const bool debug = false, msgBoxes = false;
            const string debugName = "MainWindow::updateAllWindowsWhenTasksComplete():";
            if (debug) debugEcho(debugName + "Start");
            if (msgBoxes) System.Windows.MessageBox.Show(debugName + "Start");

            //while (true)
            //{
                await Task.Run(() =>
                {
                    while (getPendingTasks() > 0) ;
                });
                if (debug) debugEcho(debugName + "Updating windows now.");
                if (msgBoxes) System.Windows.MessageBox.Show(debugName + "End");
                updateAllWindows();
                //await Task.Delay(1000);
            //}
        }

        //Different from above; watches the list of pending tasks and calls updateAllWindowsWhenTasksComplete() whenever
        //the list of pending tasks goes from non-zero back to zero
        private async void updateAllWindowsWhenTasksReachZero()
        {
            const bool debug = false;
            const string debugName = "MainWindow::updateAllWindowsWhenTasksReachZero():";
            int lastTasks = 0;
            while (true)
            {
                if (lastTasks > 0 && getPendingTasks() == 0)
                {
                    if (debug) debugEcho(debugName + "Activated");
                    updateAllWindowsWhenTasksComplete();
                }
                lastTasks = getPendingTasks();
                await Task.Delay(250);
            }
        }

        //Null catches are in case the task list changes in size when this is running
        public int getPendingTasks()
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
            for (int i = 0; i < auditTasks.Count; i++)
            {
                try
                {
                    if (auditTasks[i] == null) break;
                    if (!auditTasks[i].IsCompleted) tasks++;
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
            return tasks;
        }

        //Removes all completed tasks from fileAddTasks and burnQueueTasks
        private async void cleanUpTaskLists()
        {
            const string debugName = "MainWindow::cleanUpTaskLists():";
            const bool debug = false, msgBoxes = false;

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
                            if (fileAddTasks[i] == null) break;
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
                            if (burnQueueTasks[i] == null) break;
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

                    for (int i = 0; i < folderAddTasks.Count; i++)
                    {
                        try
                        {
                            if (folderAddTasks[i] == null) break;
                            if (i < folderAddTasks.Count && folderAddTasks[i].IsCompleted)
                            {
                                folderAddTasks.RemoveAt(i);
                                i = -1;
                            }
                            else if (i > folderAddTasks.Count)
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
                while ((getPendingTasks() == 0 && fileAddTasks.Count > 0 || burnQueueTasks.Count > 0 || folderAddTasks.Count > 0) || rangeCheck == true);
            });

            if (debug) debugEcho(debugName + "Finish");
        }

        private async void refreshWindowsAuto()
        {
            while (true)
            {
                await Task.Run(() =>
                {
                    updateAllWindowsWhenTasksComplete();
                });
                await Task.Delay(500);
            }
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
            informUser(userMessages.TASKS_PENDING);
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
