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
using Windows.Foundation; //i think this may be necessary to use IAsyncOperation as part of the file picker 
using System.IO;


namespace DiscDoingsWPF
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string debugText = "Debug Output\n"; //Append any debug related messages to this string
        bool debugWindowOpen = false;
        public BurnPoolManager burnpool;

        public MainWindow()
        {
            InitializeComponent();
            startBurnPool();
        }

         
        private async void openDebugWindow(object sender, RoutedEventArgs e)
        {
            if (debugWindowOpen) //Come back to this later when we fix that edge case memory leak
            {
                for (int i = 0; i < Application.Current.Windows.Count; i++)
                {

                    if (Application.Current.Windows[i].Name == "DebugOutputWindow")
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

        private void startBurnPool()
        {
            string[] directories = { @"C:\Users\coldc\Downloads", @"C:\Users\coldc\Documents\testing data compare\files of various sizes" };
            //BurnPoolManager burnpool = new BurnPoolManager();
            burnpool = new BurnPoolManager();

            addDirToBurnList(ref burnpool, directories[1], false);
            TextOutput.Text = burnpool.burnPoolToString();
            burnpool = generateBurnListsForPool(burnpool, 2000000);
            populateBurnWindow();
            debugEcho("Alright");
        }




        //Add an entire directory to the burn list
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

        //Calls BurnPoolManager::generateBurnQueueButGood until all files have been sorted into burn lists
        private BurnPoolManager generateBurnListsForPool(BurnPoolManager burnPool, long volumeSize)
        {
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
            return burnPool;
        }        


        //Send any debugging-related text here
        public void debugEcho(string text)
        {
            debugText += text += "\n";
        }


        private void populateBurnWindow()
        {
            for (int i = 0; i < burnpool.burnQueue.Count; i++)
            {
                BurnViewListBox.Items.Add(burnpool.burnQueue[i].name);
            }
        }

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


        private void memtestwindow(object sender, RoutedEventArgs e)
        {
            while (true)
            {
                var memtime = new MemTestWindow();
                memtime.Owner = this;
                memtime.Show();



                memtime.Close();
                // GC.Collect();

            }
        }

        private async void OpenFilePicker(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop;

            Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
        }

    }
}
