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
using System.ComponentModel;

namespace DiscDoingsWPF
{
    /// <summary>
    /// Interaction logic for DebugOutput.xaml
    /// </summary>
    public partial class DebugOutput : Window
    {
        public DebugOutput()
        {
            InitializeComponent();
        }

        public new void Close()
        {
            //MessageBox.Show("We hidin");
            //this.Hide();
        }

        public new void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            //MessageBox.Show("We hidin");
            //this.Hide();
        }

        public new void OnClosed(EventArgs e)
        {
            //MessageBox.Show("We hidin");
            //this.Hide();
        }

        void DebugOutput_Closing(object sender, CancelEventArgs e)
        {
            //MessageBox.Show("waaaa");
            //this.Hide();
        }

        void hideThis(object sender, EventArgs e)
        {
            //MessageBox.Show("squonch");
            //this.Hide();
        }


    }
}
