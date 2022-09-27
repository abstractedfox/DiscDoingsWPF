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
using System.Windows.Shapes;

namespace DiscDoingsWPF
{
    /// <summary>
    /// Interaction logic for OneBurnViewDetails.xaml
    /// </summary>
    public partial class OneBurnViewDetails : Window
    {
        private BurnPoolManager? _burnPoolRef;
        private MainWindow _mainWindowReference;
        private int _memberInBurnQueue;

        public OneBurnViewDetails(ref BurnPoolManager burnpool, int burnQueueMember, MainWindow parentWindow)
        {
            InitializeComponent();

            _burnPoolRef = burnpool;
            _mainWindowReference = parentWindow;

            if (_burnPoolRef == null)
            {
                parentWindow.DebugEcho("OneBurnViewDetails: Passed BurnPoolManager reference is null");
            }
            else
            {
                PopulateFields(burnQueueMember);
            }
        }


        //Pass an int pointing to a member in the burnQueue list and it will populate this window with details about that OneBurn
        private void PopulateFields(int burnQueueMember)
        {
            _memberInBurnQueue = burnQueueMember; //this is set for RemoveFileFromOneBurnButtonClick, so don't remove it

            OneBurnViewDetails_BurnName.Content = _burnPoolRef.BurnQueue[burnQueueMember].Name;
            OneBurnViewDetails_VolumeSize.Content = _burnPoolRef.BurnQueue[burnQueueMember].SizeOfVolume;
            string spaceRemaining = new string("");
            spaceRemaining += _burnPoolRef.BurnQueue[burnQueueMember].SpaceUsed;
            spaceRemaining += " / ";
            spaceRemaining += _burnPoolRef.BurnQueue[burnQueueMember].SpaceRemaining;
            OneBurnViewDetails_SpaceUsedUnused.Content = spaceRemaining;
            OneBurnViewDetails_TimesBurned.Content = _burnPoolRef.BurnQueue[burnQueueMember].TimesBurned;

            OneBurnListBox.Items.Clear();
            for (int i = 0; i < _burnPoolRef.BurnQueue[burnQueueMember].Files.Count; i++)
            {
                OneBurnListBox.Items.Add(_burnPoolRef.BurnQueue[burnQueueMember].Files[i].OriginalPath);
            }
        }

        private void RemoveFileFromOneBurnButtonClick(object sender, RoutedEventArgs e)
        {
            const bool debug = false, logging = true;
            const string debugName = "OneBurnViewDetails::RemoveFileFromOneBurnButtonClick():", friendlyName = "";

            

            if (debug)
            {
                _mainWindowReference.DebugEcho(debugName + "Start");
            }

            for (int i = 0; i < OneBurnListBox.SelectedItems.Count; i++)
            {
                if (debug)
                {
                    _mainWindowReference.DebugEcho(debugName + "iteration " + i + ": memberInBurnQueue = " + _memberInBurnQueue
                        + " Members in that OneBurn: " + _burnPoolRef.BurnQueue[_memberInBurnQueue].Files.Count);
                }
                if (!_burnPoolRef.RemoveFileFromOneBurn(_memberInBurnQueue, _burnPoolRef.BurnQueue[_memberInBurnQueue].FindFileByFullPath(OneBurnListBox.SelectedItems[i].ToString()))){
                    _mainWindowReference.DebugEcho(debugName + "Failed to successfully remove file.");
                    return;
                }
            }

            

            PopulateFields(_memberInBurnQueue);
        }

        private void OneBurnListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            const string debugName = "OneBurnViewDetails::OneBurnListBox_MouseDoubleClick():";
            if (OneBurnListBox.SelectedItems.Count != 1)
            {
                return;
            }

            int? filePropsToGet = _burnPoolRef.FindFileByFullPath(OneBurnListBox.SelectedItem.ToString());
            if (filePropsToGet == null)
            {
                _mainWindowReference.DebugEcho(debugName + "Invalid selection");
                return;
            }

            var fileViewDetails = new FilePropsViewDetails(ref _burnPoolRef, _mainWindowReference, (int)filePropsToGet);
            fileViewDetails.Owner = this;
            fileViewDetails.Topmost = false;
            //this.Name = "OneBurnViewDetails";

            fileViewDetails.Show();
        }
    }
}
