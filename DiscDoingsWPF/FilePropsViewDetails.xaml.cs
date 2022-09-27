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
        private BurnPoolManager? _burnPoolRef;
        private MainWindow _mainWindowReference;

        public FilePropsViewDetails(ref BurnPoolManager burnpool, MainWindow parentWindow, int allFilesIndex)
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
                PopulateFields(allFilesIndex);
            }
        }

        public void PopulateFields(int allFilesIndex)
        {
            FilePropsDetailsListBox.Items.Clear();

            FilePropsViewDetails_FileName.Content = _burnPoolRef.AllFiles[allFilesIndex].FileName;
            FilePropsViewDetails_OriginalPath.Text = _burnPoolRef.AllFiles[allFilesIndex].OriginalPath;
            FilePropsViewDetails_Size.Content = _burnPoolRef.AllFiles[allFilesIndex].Size;

            string checksum = "";
            for (int i = 0; i < _burnPoolRef.AllFiles[allFilesIndex].Checksum.Length; i++)
            {
                checksum += _burnPoolRef.AllFiles[allFilesIndex].Checksum[i].ToString();
            }
            FilePropsViewDetails_Checksum.Text = checksum;
            FilePropsViewDetails_DateAdded.Content = _burnPoolRef.AllFiles[allFilesIndex].TimeAdded;
            FilePropsViewDetails_LastModified.Content = _burnPoolRef.AllFiles[allFilesIndex].LastModified;
            FilePropsViewDetails_Status.Content = _burnPoolRef.AllFiles[allFilesIndex].FileStatus;

            if (_burnPoolRef.AllFiles[allFilesIndex].DiscsBurned.Count == 0)
            {
                FilePropsDetailsListBox.Items.Add("This file has not been burned to any discs.");
            }
            else
            {
                for (int i = 0; i < _burnPoolRef.AllFiles[allFilesIndex].DiscsBurned.Count; i++)
                {
                    FilePropsDetailsListBox.Items.Add(_burnPoolRef.AllFiles[allFilesIndex].DiscsBurned[i]);
                }
            }
        }
    }
}
