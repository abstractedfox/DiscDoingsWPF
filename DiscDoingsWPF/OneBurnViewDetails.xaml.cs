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
        BurnPoolManager? burnpoolref;
        MainWindow mainWindowReference;
        int memberInBurnQueue;

        public OneBurnViewDetails(ref BurnPoolManager burnpool, int burnQueueMember, MainWindow parentWindow)
        {
            InitializeComponent();

            burnpoolref = burnpool;
            mainWindowReference = parentWindow;

            if (burnpoolref == null)
            {
                parentWindow.debugEcho("OneBurnViewDetails: Passed BurnPoolManager reference is null");
            }
            else
            {
                populateFields(burnQueueMember);
            }
        }


        //Pass an int pointing to a member in the burnQueue list and it will populate this window with details about that OneBurn
        private void populateFields(int burnQueueMember)
        {
            memberInBurnQueue = burnQueueMember; //this is set for RemoveFileFromOneBurnButtonClick, so don't remove it

            OneBurnViewDetails_BurnName.Content = burnpoolref.burnQueue[burnQueueMember].name;
            OneBurnViewDetails_VolumeSize.Content = burnpoolref.burnQueue[burnQueueMember].sizeOfVolume;
            string spaceRemaining = new string("");
            spaceRemaining += burnpoolref.burnQueue[burnQueueMember].spaceUsed;
            spaceRemaining += " / ";
            spaceRemaining += burnpoolref.burnQueue[burnQueueMember].spaceRemaining;
            OneBurnViewDetails_SpaceUsedUnused.Content = spaceRemaining;

            OneBurnListBox.Items.Clear();
            for (int i = 0; i < burnpoolref.burnQueue[burnQueueMember].files.Count; i++)
            {
                OneBurnListBox.Items.Add(burnpoolref.burnQueue[burnQueueMember].files[i].originalPath);
            }
        }

        private void RemoveFileFromOneBurnButtonClick(object sender, RoutedEventArgs e)
        {
            const bool debug = true;
            const string debugName = "OneBurnViewDetails::RemoveFileFromOneBurnButtonClick():";

            if (debug)
            {
                mainWindowReference.debugEcho(debugName + "Start");
            }

            for (int i = 0; i < OneBurnListBox.SelectedItems.Count; i++)
            {
                if (debug)
                {
                    mainWindowReference.debugEcho(debugName + "iteration " + i + ": memberInBurnQueue = " + memberInBurnQueue
                        + " Members in that OneBurn: " + burnpoolref.burnQueue[memberInBurnQueue].files.Count);
                }
                if (!burnpoolref.removeFileFromOneBurn(memberInBurnQueue, burnpoolref.burnQueue[memberInBurnQueue].findFileByFullPath(OneBurnListBox.SelectedItems[i].ToString()))){
                    mainWindowReference.debugEcho(debugName + "Failed to successfully remove file.");
                    return;
                }
            }

            

            populateFields(memberInBurnQueue);
        }
    }
}
