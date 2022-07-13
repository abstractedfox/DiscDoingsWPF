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

        public OneBurnViewDetails(ref BurnPoolManager burnpool, int burnQueueMember, MainWindow parentWindow)
        {
            InitializeComponent();

            burnpoolref = burnpool;

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
            OneBurnViewDetails_BurnName.Content = burnpoolref.burnQueue[burnQueueMember].name;
            OneBurnViewDetails_VolumeSize.Content = burnpoolref.burnQueue[burnQueueMember].sizeOfVolume;
            string spaceRemaining = new string("");
            spaceRemaining += burnpoolref.burnQueue[burnQueueMember].spaceUsed;
            spaceRemaining += " / ";
            spaceRemaining += burnpoolref.burnQueue[burnQueueMember].spaceRemaining;
            OneBurnViewDetails_SpaceUsedUnused.Content = spaceRemaining;

            for (int i = 0; i < burnpoolref.burnQueue[burnQueueMember].files.Count; i++)
            {
                OneBurnListBox.Items.Add(burnpoolref.burnQueue[burnQueueMember].files[i].fileName);
            }
        }
    }
}
