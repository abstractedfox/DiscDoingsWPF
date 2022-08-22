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
    /// Interaction logic for FilePropsViewDetails.xaml
    /// </summary>
    public partial class FilePropsViewDetails : Window
    {
        BurnPoolManager? burnpoolref;
        MainWindow mainWindowReference;

        public FilePropsViewDetails(ref BurnPoolManager burnpool, MainWindow parentWindow, int allFilesIndex)
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
                populateFields(allFilesIndex);
            }
        }

        public void populateFields(int allFilesIndex)
        {
            FilePropsDetailsListBox.Items.Clear();

            FilePropsViewDetails_FileName.Content = burnpoolref.allFiles[allFilesIndex].fileName;
            FilePropsViewDetails_OriginalPath.Text = burnpoolref.allFiles[allFilesIndex].originalPath;
            FilePropsViewDetails_Size.Content = burnpoolref.allFiles[allFilesIndex].size;

            string checksum = "";
            for (int i = 0; i < burnpoolref.allFiles[allFilesIndex].checksum.Length; i++)
            {
                checksum += burnpoolref.allFiles[allFilesIndex].checksum[i].ToString();
            }
            FilePropsViewDetails_Checksum.Text = checksum;
            FilePropsViewDetails_DateAdded.Content = burnpoolref.allFiles[allFilesIndex].timeAdded;
            FilePropsViewDetails_LastModified.Content = burnpoolref.allFiles[allFilesIndex].lastModified;
            FilePropsViewDetails_Status.Content = burnpoolref.allFiles[allFilesIndex].fileStatus;

            if (burnpoolref.allFiles[allFilesIndex].discsBurned.Count == 0)
            {
                FilePropsDetailsListBox.Items.Add("This file has not been burned to any discs.");
            }
            else
            {
                for (int i = 0; i < burnpoolref.allFiles[allFilesIndex].discsBurned.Count; i++)
                {
                    FilePropsDetailsListBox.Items.Add(burnpoolref.allFiles[allFilesIndex].discsBurned[i]);
                }
            }
        }
    }
}
