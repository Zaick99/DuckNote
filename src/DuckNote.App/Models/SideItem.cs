namespace DuckNote.App.Models;

public sealed class SideItem : Observable
{
    private string _key = string.Empty;
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _subVis = "Collapsed";
    private string _dot = string.Empty;
    private string _ip = string.Empty;
    private object? _para;

    public string Key { get => _key; set => Set(ref _key, value); }
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Subtitle { get => _subtitle; set => Set(ref _subtitle, value); }

    public string SubVis { get => _subVis; set => Set(ref _subVis, value); }

    public string Dot { get => _dot; set => Set(ref _dot, value); }
    public string IP { get => _ip; set => Set(ref _ip, value); }

    public object? Para { get => _para; set => Set(ref _para, value); }

    public override string ToString() => Title;
}
