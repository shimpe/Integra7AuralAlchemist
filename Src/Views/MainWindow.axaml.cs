using System.Threading.Tasks;
using FluentAvalonia.UI.Windowing;
using Integra7AuralAlchemist.ViewModels;
using ReactiveUI;

namespace Integra7AuralAlchemist.Views;

public partial class MainWindow : FAAppWindow, IViewFor<MainWindowViewModel>
{
    private MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        TitleBar.ExtendsContentIntoTitleBar = true;
        // FluentAvalonia 3.0 removed TitleBar.TitleBarHitTestType (and the
        // TitleBarHitTestType enum). If interactive controls in the extended
        // title-bar region stop responding to input, revisit hit-testing for FA 3.0.
    }

    public MainWindowViewModel ViewModel
    {
        get => _viewModel;
        set
        {
            _viewModel = value;
            DataContext = value;
        }
    }

    object IViewFor.ViewModel
    {
        get => ViewModel;
        set => ViewModel = (MainWindowViewModel)value;
    }

    public void RegisterDialogHandler()
    {
        this.WhenActivated(action =>
            action(ViewModel!.ShowSaveUserToneDialog.RegisterHandler(DoShowDialogAsync)));
    }

    private async Task DoShowDialogAsync(IInteractionContext<SaveUserToneViewModel,
        UserToneToSave?> interaction)
    {
        var dialog = new SaveUserToneDialog();
        dialog.DataContext = interaction.Input;

        var result = await dialog.ShowDialog<UserToneToSave?>(this);
        interaction.SetOutput(result);
    }
}