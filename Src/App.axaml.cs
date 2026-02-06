using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Integra7AuralAlchemist.Models.Bootstrapping;
using Integra7AuralAlchemist.ViewModels;
using Integra7AuralAlchemist.Views;

namespace Integra7AuralAlchemist;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    private void EnsureDatabase()
    {
        string commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(commonAppData, "Integra7AuralAlchemist");
        if (!Directory.Exists(appFolder))
        {
            Directory.CreateDirectory(appFolder);
        }
        string dbPath = Path.Combine(appFolder, "Integra7AuralAlchemist.db");
        string idxPath = Path.Combine(appFolder, "Integra7AuralAlchemist.idx");
        if (!File.Exists(dbPath))
        {
            var p = new Integra7Parameters();
            Integra7JsonGzipDumper.Dump(dbPath, idxPath, p.Parameters);
        }
    }


    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            EnsureDatabase(); // one-time
            
            var vm = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };
            var mw = desktop.MainWindow as MainWindow;
            mw.ViewModel = vm;
            mw.RegisterDialogHandler();
            _ = vm.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}