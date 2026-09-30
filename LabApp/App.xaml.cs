using System.Windows;
using LabApp.Ui;

namespace LabApp
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Тему включаем до создания главного окна, чтобы оно сразу открылось в нужных цветах
            AppTheme.Apply(AppTheme.LoadPreference());
        }
    }
}
