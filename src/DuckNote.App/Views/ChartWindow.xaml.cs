using System.Windows;
using System.Windows.Input;
using DuckNote.App.Editor;
using DuckNote.App.Theme;

namespace DuckNote.App.Views;

public partial class ChartWindow : Window
{
    private readonly ChartView _preview;

    public ChartWindow(string written, string theme, ResourceDictionary ownerResources, EditorBrushes brushes)
    {
        InitializeComponent();

        Resources.MergedDictionaries.Add(ownerResources);
        ThemeTokens.Apply(Resources, theme);

        _preview = new ChartView(brushes);
        CPreview.Content = _preview;

        CText.Text = written;
        Redraw();

        CText.TextChanged += (_, _) => Redraw();

        ContentRendered += (_, _) =>
        {
            Motion.OpenDialog(CRoot);
            CText.Focus();
        };

        CTitleBar.MouseLeftButtonDown += (_, _) =>
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        };

        CClose.Click += (_, _) => Dismiss();
        CCancel.Click += (_, _) => Dismiss();
        COk.Click += (_, _) => Accept();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Dismiss();
            }
        };
    }

    public string Written { get; private set; } = string.Empty;

    private void Redraw() => _preview.Plan = ChartPlan.Read(CText.Text);

    private void Accept()
    {
        Written = CText.Text;
        Motion.CloseDialog(this, CRoot, true);
    }

    private void Dismiss() => Motion.CloseDialog(this, CRoot, false);
}
