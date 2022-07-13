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
    /// Interaction logic for MemTestWindow.xaml
    /// </summary>
    public partial class MemTestWindow : Window
    {
        public MemTestWindow()
        {
            InitializeComponent();
        }

        public void Close()
        {
            this.Hide();
        }
    }
}
