using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YoutubeDownloader.App.Interop;
using YoutubeDownloader.App.Services;
using YoutubeDownloader.App.ViewModels;

namespace YoutubeDownloader.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    /// <summary>Ctrl+1/2 trocam de tela; Ctrl+V fora de um campo de texto cola o link e já analisa.</summary>
    protected override async void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            var index = e.Key - Key.D1;
            if (index >= 0 && index < _viewModel.Navigation.Count)
            {
                _viewModel.SelectedNav = _viewModel.Navigation[index];
                e.Handled = true;
                return;
            }

            if (e.Key == Key.V && Keyboard.FocusedElement is not TextBox && !_viewModel.IsSetupOpen)
            {
                e.Handled = true;
                _viewModel.SelectedNav = _viewModel.Navigation[0];
                if (Shell.ClipboardText() is { } text && !string.IsNullOrWhiteSpace(text))
                    await _viewModel.Downloads.AcceptUrlAsync(text);
                return;
            }
        }

        base.OnPreviewKeyDown(e);
    }

    private static string? DroppedText(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.UnicodeText) ? e.Data.GetData(DataFormats.UnicodeText) as string
        : e.Data.GetDataPresent(DataFormats.Text) ? e.Data.GetData(DataFormats.Text) as string
        : null;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedText(e) is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (DroppedText(e) is not { } text || _viewModel.IsSetupOpen)
            return;
        e.Handled = true;
        _viewModel.SelectedNav = _viewModel.Navigation[0];
        await _viewModel.Downloads.AcceptUrlAsync(text);
    }
}
