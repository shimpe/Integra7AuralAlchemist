using System.Reactive.Concurrency;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Integra7AuralAlchemist.ViewModels;
using Integra7AuralAlchemist.Views;

namespace Integra7AuralAlchemist;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // ReactiveUI 24 ships two distributions, and UseReactiveUI() configures the core one. This
        // application uses the System.Reactive distribution (see the aliases in the csproj), whose
        // RxSchedulers.MainThreadScheduler documents itself as defaulting to Sequencer.Default -- a
        // background scheduler. Every ObserveOn in the view models names that property, so leaving it
        // unset marshals DynamicData's SortAndBind onto a pool thread, which raises CollectionChanged
        // off the UI thread and kills the process inside Avalonia's WeakEvent with a
        // NullReferenceException. Set here rather than in BuildAvaloniaApp because the dispatcher this
        // binds to has to exist first.
        ReactiveUI.Reactive.RxSchedulers.MainThreadScheduler =
            new SynchronizationContextScheduler(new AvaloniaSynchronizationContext());

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Line below is needed to remove Avalonia data validation.
            // Without this line you will get duplicate validations from both Avalonia and CT
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