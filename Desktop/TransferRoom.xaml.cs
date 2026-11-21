using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace PersonalDownloader.Desktop;

public partial class TransferRoom : UserControl
{
    static readonly Brush Green = new SolidColorBrush(Color.FromRgb(191, 247, 115));
    static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(101, 214, 255));
    static readonly Brush Neutral = Brushes.LightSlateGray;
    readonly Canvas hardware;
    static readonly Brush[] Colors = [Green, Blue, Brushes.SandyBrown];
    static readonly string[] Devices = ["Wi-Fi", "USB tether", "LAN"];
    readonly bool[] moving = new bool[3];
    readonly Brush?[] cableColors = new Brush?[3];
    Dictionary<string,int> hardwareLanes = [];
    string[] selected = [];
    string[] rangeNames = [];
    readonly Dictionary<string, TextBlock> rateLabels = [];
    readonly List<(ProgressBar Range, ProgressBar File, ProgressBar Merge)> bars = [];
    TransferRange[]? displayed;
    string[] routes = [];
    QueueRow? current;

    public TransferRoom()
    {
        InitializeComponent();
        hardware = (Canvas)Application.LoadComponent(new Uri("/LinkFoundry;component/RoomHardware.xaml", UriKind.Relative));
        HardwareHost.Content = hardware;
        IsVisibleChanged += (_, _) => Display(current);
        Display(null);
    }

    public void Display(QueueRow? row)
    {
        current = row;
        var ranges = row?.Ranges ?? [];
        if (!ReferenceEquals(displayed, ranges) || !ranges.Select(r => r.Route).SequenceEqual(rangeNames)) Rebuild(ranges);
        Title.Text = row?.Name ?? "Transfer room";
        Source.Text = row == null ? "Add a download to see its path to disk." :
            $"{new Uri(row.Job.Url).Host}    /    {(row.Total.HasValue ? QueueRow.Format(row.Total.Value) : "Size unknown")}";
        string activity = row?.Activity ?? "";
        bool transferring = row?.State == "Downloading" && activity.StartsWith("Downloading");
        bool complete = row?.State == "Completed";
        Phase.Text = row?.State != "Downloading" ? row?.State ?? "Idle" :
            activity.StartsWith("Retrying") ? "Reconnecting" :
            activity.StartsWith("Assembling") ? "Assembling" : activity == "" ? "Connecting" : activity.Split('·')[0].Trim();
        Amount.Text = row?.Amount ?? "0 B";
        DiskTitle.Text = complete ? "File ready" : activity.StartsWith("Assembling") && row?.State == "Downloading" ? activity : "File on disk";
        foreach (var (route, label) in rateLabels)
            label.Text = transferring && route != "On disk" ? QueueRow.Format(row!.Speeds.GetValueOrDefault(route)) + "/s" :
                route == "On disk" ? QueueRow.Format(ranges.Where(r => r.Route == route).Sum(r => r.Saved)) + " saved" :
                QueueRow.Format(ranges.Where(r => r.Route == route).Sum(r => r.Saved - r.Existing)) + " received";
        for (int i = 0; i < ranges.Length; i++)
        {
            var r = ranges[i];
            var b = bars[i];
            double percent = complete ? 100 : r.Length > 0 ? 100.0 * r.Saved / r.Length.Value : 0;
            b.Range.Value = b.File.Value = percent;
            b.Range.ToolTip = b.File.ToolTip = $"{r.Saved:N0} bytes saved";
            b.Merge.Value = complete ? 100 : r.Length > 0 ? 100.0 * r.Assembled / r.Length.Value : 0;
        }
        for (int i = 0; i < 4; i++)
        {
            var tile = (TranslateTransform3D)hardware.FindName($"Range{i}");
            bool landed = i < ranges.Length && (complete || ranges[i].Assembled > 0);
            tile.OffsetY = landed ? complete ? 0 : .65 * (1 - bars[i].Merge.Value / 100) : -15;
        }
        TextBlock[] pins = [Pin0, Pin1, Pin2];
        for (int lane = 0; lane < 3; lane++)
        {
            var assigned = routes.Where(r => Lane(r) == lane).ToArray();
            var receiving = ranges.Where(r => assigned.Contains(r.Route) && r.Saved > r.Existing).ToArray();
            
            bool flowing = transferring && assigned.Any(r => row!.Speeds.GetValueOrDefault(r) > 0);
            bool connected = flowing || transferring && receiving.Length > 0;
            var armed = selected.Where(r => Lane(r) == lane).ToArray();
            var letters = ranges.Select((r,i) => (r,i)).Where(p => assigned.Contains(p.r.Route)).Select(p => (char)('A'+p.i));
            string label = connected ? $"{string.Join(" / ", assigned)} · {string.Join(" + ", letters)}" :
                armed.Length > 0 ? $"{string.Join(" / ", armed)} · Selected" : $"{Devices[lane]} · Disconnected";
            pins[lane].Text = label;
            pins[lane].ToolTip = label;
            pins[lane].Foreground = connected || armed.Length > 0 ? Colors[lane] : Neutral;
            Brush tint = connected ? Colors[lane] : armed.Length > 0 ? Brushes.SlateGray : Brushes.DarkSlateGray;
            if (tint != cableColors[lane])
            {
                var cable = (GeometryModel3D)hardware.FindName($"Cable{lane}");
                cable.Material = connected ? new EmissiveMaterial(tint) : new DiffuseMaterial(tint);
                cableColors[lane] = tint;
            }
            Animate(lane, IsVisible && SystemParameters.ClientAreaAnimation && flowing);
        }
    }

    public void SetAdapters(IEnumerable<RackSlot> available, IEnumerable<RackSlot> chosen)
    {
        hardwareLanes = available.ToDictionary(s => s.Name, s => s.HardwareLane, StringComparer.OrdinalIgnoreCase);
        selected = chosen.Select(s => s.Name).ToArray();
        displayed = null;
        Display(current);
    }

    int Lane(string route) => route == "Windows route" ? -1 : hardwareLanes.TryGetValue(route, out int lane) ? lane :
        route.Contains("tether", StringComparison.OrdinalIgnoreCase) ? 1 :
        route.Contains("ethernet", StringComparison.OrdinalIgnoreCase) || route == "LAN" ? 2 : 0;

    void Animate(int lane, bool run)
    {
        if (moving[lane] == run) return;
        moving[lane] = run;
        var track = (Storyboard)hardware.Resources[$"Lane{lane}"];
        if (run) track.Begin(hardware, true);
        else track.Remove(hardware);
        for (int i = 0; i < (lane == 0 ? 4 : 3); i++)
            ((TranslateTransform3D)hardware.FindName($"Packet{lane}_{i}Hide")).OffsetY = run ? 0 : -15;
    }

    static ProgressBar Bar(Brush color, int height) => new()
    {
        Height = height, Maximum = 100, Foreground = color,
        BorderThickness = new Thickness(0), Background = new SolidColorBrush(Color.FromRgb(52, 70, 82))
    };

    void Rebuild(TransferRange[] ranges)
    {
        displayed = ranges;
        rangeNames = ranges.Select(r => r.Route).ToArray();
        routes = ranges.Select(r => r.Route).Where(r => r != "On disk").Distinct().ToArray();
        Waiting.Visibility = ranges.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RangesPanel.Children.Clear();
        FileParts.Children.Clear();
        rateLabels.Clear();
        bars.Clear();
        var groups = new Dictionary<string, StackPanel>();
        foreach (var range in ranges)
        {
            var color = range.Route == "On disk" || Lane(range.Route) < 0 ? Neutral : Colors[Lane(range.Route)];
            var tile = (GeometryModel3D)hardware.FindName($"Range{bars.Count}Tile");
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(Brushes.DarkSlateGray));
            material.Children.Add(new EmissiveMaterial(color));
            material.Freeze();
            tile.Material = material;
            if (!groups.TryGetValue(range.Route, out var panel))
            {
                panel = new StackPanel();
                groups.Add(range.Route, panel);
                RangesPanel.Children.Add(panel);
                var header = new DockPanel { Margin = new Thickness(0, 8, 0, 4) };
                var rate = new TextBlock { Foreground = color, FontSize = 14 };
                rateLabels.Add(range.Route, rate);
                DockPanel.SetDock(rate, Dock.Right);
                header.Children.Add(rate);
                header.Children.Add(new TextBlock { Text = range.Route, FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = range.Route });
                panel.Children.Add(header);
            }
            string span = range.Length is long length ? $"{QueueRow.Format(range.Start)} – {QueueRow.Format(range.Start + length)}" : "Open-ended";
            string title = $"{(char)('A' + bars.Count)}    {span}";
            var line = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            line.Children.Add(new TextBlock { Text = title, Width = 176, FontSize = 13, Foreground = color });
            var bar = Bar(color, 8);
            line.Children.Add(bar);
            panel.Children.Add(line);
            var file = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            file.Children.Add(new TextBlock { Text = title, FontSize = 14, Margin = new Thickness(0,0,0,7) });
            var saved = Bar(color, 12);
            var merged = Bar(Brushes.WhiteSmoke, 2);
            merged.Margin = new Thickness(0,4,0,0);
            file.Children.Add(saved);
            file.Children.Add(merged);
            FileParts.Children.Add(file);
            bars.Add((bar,saved,merged));
        }
    }
}
