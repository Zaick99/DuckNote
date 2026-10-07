using System.Windows.Documents;

namespace DuckNote.App.Models;

public sealed class SideNode : Observable
{
    private string _title = string.Empty;
    private string _toggle = string.Empty;
    private string _toggleVis = "Hidden";
    private string _rails = string.Empty;
    private string _badge = string.Empty;
    private string _badgeVis = "Collapsed";
    private string _hereVis = "Collapsed";
    private string _editVis = "Collapsed";
    private string _labelVis = "Visible";
    private double _railWidth;
    private double _rowHeight = 20;

    public required string Key { get; init; }

    public required string PageId { get; init; }

    public required bool IsPage { get; init; }

    public Paragraph? Heading { get; set; }

    public List<SideNode> Children { get; } = [];

    public bool IsOpen { get; set; } = true;

    public string Title { get => _title; set => Set(ref _title, value); }

    public string Toggle { get => _toggle; set => Set(ref _toggle, value); }

    public string ToggleVis { get => _toggleVis; set => Set(ref _toggleVis, value); }

    public string Rails { get => _rails; set => Set(ref _rails, value); }

    public double RailWidth { get => _railWidth; set => Set(ref _railWidth, value); }

    public double RowHeight { get => _rowHeight; set => Set(ref _rowHeight, value); }

    public string Badge { get => _badge; set => Set(ref _badge, value); }

    public string BadgeVis { get => _badgeVis; set => Set(ref _badgeVis, value); }

    public string HereVis { get => _hereVis; set => Set(ref _hereVis, value); }

    public string EditVis { get => _editVis; set => Set(ref _editVis, value); }

    public string LabelVis { get => _labelVis; set => Set(ref _labelVis, value); }

    public override string ToString() => Title;
}
