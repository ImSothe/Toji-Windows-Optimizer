using System.Windows;

namespace Toji
{
    public partial class EulaWindow : Window
    {
        public EulaWindow()
        {
            InitializeComponent();
            NativeTheme.ApplyDarkTitleBar(this);
            BtnAccept.Click += (s, e) => { DialogResult = true; Close(); };
            BtnDecline.Click += (s, e) => { DialogResult = false; Close(); };
        }
    }
}
