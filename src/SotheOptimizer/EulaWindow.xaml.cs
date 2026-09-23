using System.Windows;

namespace Sothe
{
    public partial class EulaWindow : Window
    {
        public EulaWindow()
        {
            InitializeComponent();
            BorderlessWindow.Attach(this, RootBorder, BtnMinimize, null, BtnCloseWindow, TitleBar, HeaderBar);
            BtnAccept.Click += (s, e) => { DialogResult = true; Close(); };
            BtnDecline.Click += (s, e) => { DialogResult = false; Close(); };
        }
    }
}
