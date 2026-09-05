using System.IO;
using System.Windows;
using PersonalToolbox.Views;
using Toolbox.Core;

namespace CispTrainer;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CISP 刷题");
        Directory.CreateDirectory(dataRoot);
        var assetRoot = Path.Combine(AppContext.BaseDirectory, "Assets", "Cisp");
        var progressStore = new CispProgressStore(Path.Combine(dataRoot, "progress.json"));
        var window = new CispQuestionBankWindow(
            Path.Combine(dataRoot, "public-bank.md"),
            progressStore,
            Path.Combine(assetRoot, "public-bank.md"),
            Path.Combine(assetRoot, "pic"),
            Path.Combine(assetRoot, "PdfSets"),
            Path.Combine(assetRoot, "2026-sets.json"))
        {
            Title = "CISP 刷题",
        };
        MainWindow = window;
        window.Show();
    }
}
