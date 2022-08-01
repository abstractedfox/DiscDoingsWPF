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

    

    public partial class MainWindow : Window
    {
        string debugText = "Debug Output\n"; //Append any debug related messages to this string
        bool debugWindowOpen = false;
        
        public BurnPoolManager burnpool;
        private BurnPoolManager lastSavedInstance; //Use to keep track of whether changes have been made

        public int filesInChecksumQueue; //Keep track of how many files are waiting for checksum calculations

        const string applicationName = "Burn Manager", applicationExtension = "chris";

        public MainWindow()
        {
            
            InitializeComponent();
            
            startBurnPool();

        }

        private void test()
        {
            burnpool.generateAllBurnQueues(2000000);
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
                await Task.Delay(1000);
            }
            
        }

        

        //Goes to the ListBox in the burn view
        private void ChooseBurnList(object sender, SelectionChangedEventArgs args)
        {
            
        }

        //Initializes the currently loaded burnpool and window contents
        private void startBurnPool()
        {
            //string[] directories = { @"C:\Users\coldc\Downloads", @"C:\Users\coldc\Documents\testing data compare\files of various sizes" };

            debugEcho("Initializing burnpool");
            burnpool = new BurnPoolManager(this);
            updateMainScreenFileView();
            populateBurnWindow();

            lastSavedInstance = new BurnPoolManager(burnpool);
        }


        //Add an entire directory to the burn list.
        private bool addDirToBurnList(ref BurnPoolManager list, string dir, bool recursive)
        {
            const string debugName = "addDirToBurnList:";
            DirectoryInfo directory = new DirectoryInfo(dir);

            if (!directory.Exists)
            {
                debugEcho(debugName + "Directory " + dir + " is invalid.");
                return false;
            }

            FileInfo[] files = directory.GetFiles();

            for (int i = 0; i < files.Length; i++)
            {
                list.addFile(files[i]);
            }

            return true;
        }


        //Calls BurnPoolManager::generateBurnQueueButGood until all files have been sorted into burn lists.
        
        private BurnPoolManager generateBurnListsForPool(BurnPoolManager burnPool, long volumeSize)
        {
            debugEcho("generateBurnListsForPool: Start");
            BurnPoolManager.OneBurn aBurn = new BurnPoolManager.OneBurn();

            do
            {
                aBurn = burnPool.generateBurnQueueButGood(volumeSize);

                if (aBurn.files.Count > 0)
                {
                    burnPool.burnQueue.Add(aBurn);
                }

            }
            while (aBurn.files.Count > 0);
            debugEcho("generateBurnListsForPool: Done");
            //populateBurnWindow();
            return burnPool;
        }

        //Calls generateBurnListsForPool asynchronously
        private async Task<BurnPoolManager> generateBurnListsForPoolAsync(BurnPoolManager burnPool, long volumeSize)
        {
            debugEcho("generateBurnListsForPoolAsync: Start");
            BurnPoolManager.OneBurn aBurn = new BurnPoolManager.OneBurn();

            return await Task.Run<BurnPoolManager>(() =>
            {
                return new BurnPoolManager(generateBurnListsForPool(burnPool, volumeSize));
            });
            

            //return burnPool;
        }


        //Send any debugging-related text here
        public void debugEcho(string text)
        {
            debugText += text += "\n";
        }


        public async void debugEchoAsync(string text)
        {
            await Task.Run(() => { debugEcho(text); });
        }

        //Show details for a OneBurn in the burn view list box
        private void BurnViewListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            int? oneBurnToGet = burnpool.getBurnQueueFileByName(BurnViewListBox.SelectedItem.ToString());
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

            await generateBurnListsForPoolAsync(burnpool, volumeSize);
            updateAllWindows();
            
        }


        private void VolumeSizeTextInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string hold = VolumeSizeTextInput.Text;
            
        }

        //Opens the file picker and adds any files chosen to the burn pool.
        private async void OpenFilePicker(object sender, RoutedEventArgs e)
        {
            const bool debugVerbose = true;
            const string debugName = "OpenFilePicker:";
            FileOpenPicker picker = new FileOpenPicker();
            picker.ViewMode = PickerViewMode.List;
            //picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            //Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();

            var win32moment = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr handle = win32moment.Handle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
            IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();

            if (debugVerbose)
            {
                //MessageBox.Show("IReadOnlyList<StorageFile> files is now populated");
                debugEcho(debugName + "IReadOnlyList<StorageFile> files is now populated");
            }

            int filesPrior = burnpool.allFiles.Count;
            foreach (StorageFile file in files)
            {
                //burnpool.addFile(file);
                try
                {
                    burnpool.addFileAsync(file, ref filesInChecksumQueue);
                }
                catch
                {
                    debugEcho(debugName + "Exception while adding files to the burnpool.");
                    System.Windows.MessageBox.Show(debugName + "Exception while adding files to the burnpool.", applicationName);
                }
            }

            int previousFilesInChecksumQueue = filesInChecksumQueue;
            debugEcho(debugName + "Files in processing queue: " + filesInChecksumQueue);
            while (filesInChecksumQueue > 0)
            {
                if (filesInChecksumQueue < previousFilesInChecksumQueue)
                {
                    previousFilesInChecksumQueue = filesInChecksumQueue;
                    debugEcho(debugName + "Files in processing queue: " + filesInChecksumQueue);
                }
                await Task.Delay(1000);
                //debugEcho(debugName + "Waiting for checksums");
            }


            if (debugVerbose)
            {
                //MessageBox.Show("All files have been added to burnpool");
                debugEcho(debugName + "All files have been added to burnpool");
            }

            updateMainScreenFileView();


        }

        //Update the main file view
        private void updateMainScreenFileView()
        {
            MainWindow_FileView.Text = burnpool.burnPoolToString();
        }

        //Populate the burn window with whatever is in burnpool.burnQueue. Always blanks the burn window first.
        private void populateBurnWindow()
        {
            BurnViewListBox.Items.Clear();
            for (int i = 0; i < burnpool.burnQueue.Count; i++)
            {
                BurnViewListBox.Items.Add(burnpool.burnQueue[i].name);
            }
        }

        //Update the data display in all windows
        private void updateAllWindows()
        {
            updateMainScreenFileView();
            populateBurnWindow();
        }


        //Function to be called when the user attempts to exit or start a new file while operations are in progress
        private void operationsInProgress()
        {

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
                //debugEcho("SQUEEBY\n" + serialized);
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



                    //Top menu bar items start below


        private void FileSave_Click(object sender, RoutedEventArgs e)
        {
            const string debugName = "FileSave_Click:";
            saveFileDialog();
            /*
            string serialized = "";
            try
            {
                serialized = JsonSerializer.Serialize(burnpool);
                //debugEcho("SQUEEBY\n" + serialized);
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
                        lastSavedInstance = burnpool;
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
            */
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            const string debugName = "New_Click():";
            if (filesInChecksumQueue > 0)
            {
                debugEcho(debugName + "There are " + filesInChecksumQueue + " operations pending in the checksum queue.");
                operationsInProgress();
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
            test();
        }

        private async void FileOpen_Click(object sender, RoutedEventArgs e)
        {
            const bool debugVerbose = false;
            const string debugName = "FileLoad_Click:";

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
                    string serializedJson = await Windows.Storage.FileIO.ReadTextAsync(file);
                    if (serializedJson == null)
                    {
                        debugEcho(debugName + "Null returned from await Windows.Storage.FileIO.ReadTextAsync");
                        return;
                    }

                    burnpool = new BurnPoolManager(this);


                    burnpool = JsonSerializer.Deserialize<BurnPoolManager>(serializedJson);
                    burnpool.mainWindow = this;
                    lastSavedInstance = new BurnPoolManager(burnpool);
                    updateMainScreenFileView();
                    populateBurnWindow();
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
