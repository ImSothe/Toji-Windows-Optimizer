using System;
using System.Windows;

namespace Sothe
{
    public partial class EulaWindow : Window
    {
        public EulaWindow()
        {
            InitializeComponent();
            BorderlessWindow.Attach(this, RootBorder, BtnMinimize, null, BtnCloseWindow, TitleBar, HeaderBar);
            LangBtn.Content = Loc.T("lang.button");
            LangBtn.ToolTip = Loc.T("lang.tooltip");
            LangBtn.Click += (s, e) =>
            {
                Loc.SetLanguage(Loc.IsSpanish ? Loc.English : Loc.Spanish);
                LangBtn.Content = Loc.T("lang.button");
                LangBtn.ToolTip = Loc.T("lang.tooltip");
                Loc.Save(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SotheOptimizer"));
            };
            BtnAccept.Click += (s, e) => { DialogResult = true; Close(); };
            BtnDecline.Click += (s, e) => { DialogResult = false; Close(); };
        }
    }
}
