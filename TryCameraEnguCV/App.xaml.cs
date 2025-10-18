using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;

namespace TryCameraEnguCV
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BUSV", "VideoProcessor");

            Directory.CreateDirectory(appDataPath); // Создаём папку, если её нет

            // Передаем путь остальной логике
            AppDomain.CurrentDomain.SetData("AppDataPath", appDataPath);

            var userSelect = new UserSelectionWindow();
            userSelect.Show();
        }

    }

}
