using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media;
using Rectangle = System.Windows.Shapes.Rectangle;
using Microsoft.Win32;

namespace PersonalDownloader.Desktop;

public sealed class QueueRow(Download job) : INotifyPropertyChanged
{
    public Download Job { get; } = job;
    public string Name => Path.GetFileName(Job.Destination);
    public bool CanOpen => State == "Completed";
    public string Badge => Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 and <= 5 } ext ? ext : "FILE";
    string? product;
    public string DisplayName
    {
        get
        {
            if (!CanOpen || Badge != "EXE") return Name;
            if (product == null)
            {
                product = Name;
                try
                {
                    string? title = FileVersionInfo.GetVersionInfo(Job.Destination).ProductName;
                    if (!string.IsNullOrWhiteSpace(title)) product = title.Trim();
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            return product;
        }
    }
    public string FileDetail => $"{Name}   ·   {new Uri(Job.Url).Host}";
    public string LibrarySize => CanOpen ? Format(Bytes) : Amount;
    public string State => Job.State;
    public long Bytes { get; private set; }
    public long? Total { get; private set; }
    public string Activity { get; private set; } = "";
    public TransferRange[] Ranges { get; private set; } = [];
    public Dictionary<string, double> Speeds { get; } = [];
    readonly Dictionary<TransferRange,long> previous = [];
    public double Percent => State == "Completed" ? 100 : Total > 0 ? 100.0 * Bytes / Total.Value : 0;
    public string Amount => Total.HasValue ? $"{Format(Bytes)} / {Format(Total.Value)}" : Format(Bytes);
    public static string Format(double n) => n switch
    {
        >= 1073741824 => $"{n / 1073741824:F2} GiB",
        >= 1048576 => $"{n / 1048576:F1} MiB",
        >= 1024 => $"{n / 1024:F0} KiB",
        _ => $"{n:F0} B"
    };
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update(TransferUpdate p, double elapsed = 0)
    {
        (Bytes, Total, Activity) = (p.Bytes, p.Total, p.Activity);
        if (p.Ranges != null && !ReferenceEquals(Ranges, p.Ranges))
        {
            Ranges = p.Ranges;
            previous.Clear();
        }
        Speeds.Clear();
        foreach (var range in Ranges)
        {
            long saved = range.Saved;
            long delta = saved - previous.GetValueOrDefault(range, range.Existing);
            if (elapsed > 0 && p.Activity.StartsWith("Downloading") && State == "Downloading")
                Speeds[range.Route] = Speeds.GetValueOrDefault(range.Route) + Math.Max(0, delta) / elapsed;
            previous[range] = saved;
        }
        Changed();
    }
    public void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));
}

public partial class Dashboard : Window
{
    readonly QueueStore store;
    readonly ObservableCollection<QueueRow> rows = [];
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    QueueRow? active;
    CancellationTokenSource? stop;
    TransferUpdate? latest;
    Task? work;
    bool exiting;
    long previousTick;
    readonly Queue<double[]> history = new();

    public Dashboard() : this(new QueueStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalDownloader", "Queue"))) { }

    readonly Action<string> launchFile;

    public Dashboard(QueueStore queue, Action<string>? launcher = null)
    {
        launchFile = launcher ?? (path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
        store = queue;
        InitializeComponent();
        Jobs.ItemsSource = rows;
        foreach (var job in store.Load(s => MessageBox.Show(s)))
        {
            if (job.State == "Finalizing")
            {
                try { Transfer.RecoverFinalization(job, store); }
                catch (Exception e) { job.State = "Failed"; job.Detail = e.Message; }
            }
            if (job.State == "Downloading") job.State = "Queued";
            var row = new QueueRow(job);
            long bytes = job.State == "Completed" && File.Exists(job.Destination) ? new FileInfo(job.Destination).Length :
                Enumerable.Range(0, job.Parts).Where(i => File.Exists(job.Part(i))).Sum(i => new FileInfo(job.Part(i)).Length);
            row.Update(new(bytes, job.Size, "", TransferRange.Restore(job)));
            rows.Add(row);
        }
        RefreshNetworks(this, new RoutedEventArgs());
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Closing += OnClosing;
        Loaded += (_, _) => { Jobs.SelectedItem = rows.FirstOrDefault(r => r.State == "Queued") ?? rows.FirstOrDefault(); Refresh(); Schedule(); };
    }

    public void Receive(string link)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (link.Length == 0) return;
        try { Url.Text = Download.ParseUrl(link).AbsoluteUri; Url.Focus(); }
        catch (ArgumentException e) { Message.Text = e.Message; }
    }

    void RefreshNetworks(object sender, RoutedEventArgs e)
    {
        var selected = Networks.SelectedItems.Cast<RackSlot>().Select(a => a.Id).ToArray();
        var adapters = Connections.List().Select(a => new RackSlot(a)).ToArray();
        Networks.ItemsSource = adapters;
        foreach (var id in selected)
            if (adapters.FirstOrDefault(a => a.Id == id) is RackSlot slot) Networks.SelectedItems.Add(slot);
        UpdateSelection();
    }
    void NetworkSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelection();
    void UpdateSelection()
    {
        foreach (RackSlot slot in Networks.Items) slot.Select(Networks.SelectedItems.IndexOf(slot));
        int count = Networks.SelectedItems.Count;
        Room.SetAdapters(Networks.Items.Cast<RackSlot>(), Networks.SelectedItems.Cast<RackSlot>());
        NetworkSummary.Text = count == 0 ? $"Automatic routing  ·  {Networks.Items.Count} available" :
            $"{count} of {Networks.Items.Count} adapters selected";
    }
    async void Add(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.IsEnabled = false;
        try
        {
            var uri = Download.ParseUrl(Url.Text.Trim());
            var interfaces = Networks.SelectedItems.Cast<RackSlot>().Select(a => a.Id).ToArray();
            string name = DownloadName.FromUrl(uri) ?? "download";
            button.Content = "Finding filename…";
            using var client = Connections.Create(interfaces.FirstOrDefault());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try { name = await DownloadName.ResolveAsync(client, uri, timeout.Token); }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) { }
            if (!IsVisible) return;
            string extension = Path.GetExtension(name);
            var picker = new SaveFileDialog { FileName = name, Title = "Choose download destination", OverwritePrompt = false,
                AddExtension = extension.Any(char.IsLetter), DefaultExt = extension, Filter = "All files|*.*" };
            if (picker.ShowDialog(this) != true) return;
            if (File.Exists(picker.FileName) || rows.Any(r => string.Equals(r.Job.Destination, picker.FileName, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("That name is already in use. Choose another destination.");
            var row = new QueueRow(new Download { Url = uri.AbsoluteUri, Destination = picker.FileName,
                Interfaces = interfaces });
            store.Save(row.Job);
            rows.Add(row);
            Jobs.SelectedItem = row;
            Url.Clear();
            Schedule();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Add download"); }
        finally { button.Content = "＋  Add download"; button.IsEnabled = true; }
    }
    void Schedule()
    {
        if (exiting || active != null) return;
        var next = rows.FirstOrDefault(r => r.State == "Queued");
        if (next == null) { Refresh(); return; }
        active = next;
        work = Run(next);
    }
    async Task Run(QueueRow row)
    {
        stop = new();
        var token = stop.Token;
        latest = null;
        history.Clear();
        Graph.Children.Clear();
        previousTick = Stopwatch.GetTimestamp();
        try
        {
            row.Job.State = "Downloading";
            row.Update(new(row.Bytes, row.Total, "Connecting"));
            row.Job.Detail = "";
            store.Save(row.Job);
            Refresh();
            await Task.Run(() => new Transfer(store).RunAsync(row.Job, p => Interlocked.Exchange(ref latest, p), token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { row.Job.State = exiting ? "Queued" : "Paused"; }
        catch (Exception e) { row.Job.State = "Failed"; row.Job.Detail = e.Message; }
        finally
        {
            Tick();
            try { store.Save(row.Job); }
            catch (Exception e) { MessageBox.Show(this, e.Message, "Could not save queue"); }
            stop.Dispose();
            stop = null;
            active = null;
            Rate.Text = "0 B/s";
            RouteSpeeds.Text = "";
            row.Changed();
            Refresh();
            if (!exiting) _ = Dispatcher.BeginInvoke(Schedule);
        }
    }
    void Tick()
    {
        if (active == null) return;
        var p = Volatile.Read(ref latest);
        if (p == null) return;
        var elapsed = Stopwatch.GetElapsedTime(previousTick).TotalSeconds;
        previousTick = Stopwatch.GetTimestamp();
        active.Update(p, elapsed);
        UpdateGraph(active);
        if (active.State == "Completed" && !p.Total.HasValue)
            active.Update(p with { Total = active.Job.Size });
        Refresh();
    }
    void Refresh()
    {
        var row = Jobs.SelectedItem as QueueRow;
        QueueTab.Header = $"Library  ({rows.Count})";
        SelectedName.Text = row?.Name ?? "No download selected";
        SelectedState.Text = row?.State ?? "";
        Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Counts.Text = $"{rows.Count(r => r.State == "Queued")} queued  ·  {rows.Count(r => r.State == "Completed")} finished";
        PauseButton.IsEnabled = row?.State is "Queued" or "Downloading";
        ResumeButton.IsEnabled = row?.State is "Paused" or "Failed";
        FolderButton.IsEnabled = row != null;
        RemoveButton.IsEnabled = row != null && row != active;
        Message.Text = row == null ? "" : row.Job.Detail.Length > 0 ? row.Job.Detail :
            row == active ? row.Activity : row.Job.Destination;
        Room.Display(row ?? active);
        row?.Changed();
    }
    void UpdateGraph(QueueRow row)
    {
        var routes = row.Speeds.Where(p => p.Key != "On disk").ToArray();
        var sampleRates = new double[4];
        foreach (var (route, speed) in routes)
        {
            var slot = Networks.Items.Cast<RackSlot>().FirstOrDefault(s => s.Name == route);
            sampleRates[slot?.HardwareLane ?? 3] += speed;
        }
        double total = routes.Sum(p => p.Value);
        Rate.Text = QueueRow.Format(total) + "/s";
        RouteSpeeds.Text = string.Join(Environment.NewLine, routes.Select(p => $"{QueueRow.Format(p.Value)}/s   {p.Key}"));
        if (history.Count == 32) history.Dequeue();
        history.Enqueue(sampleRates);
        double peak = Math.Max(1, history.Max(p => p.Sum()));
        Graph.Children.Clear();
        double x = (32-history.Count)*7;
        foreach (var sample in history)
        {
            double bottom = 42;
            Brush[] colors = [Brushes.LightGreen, Brushes.SkyBlue, Brushes.SandyBrown, Brushes.LightSlateGray];
            for (int i = 0; i < sample.Length; i++)
            {
                var value = sample[i];
                var color = colors[i];
                double height = value / peak * 40;
                if (height <= 0) continue;
                bottom -= height;
                var bar = new Rectangle { Width = 4, Height = height, Fill = color, RadiusX = 1, RadiusY = 1 };
                Canvas.SetLeft(bar,x);
                Canvas.SetTop(bar,bottom);
                Graph.Children.Add(bar);
            }
            x += 7;
        }
    }
    void SelectJob(object sender, SelectionChangedEventArgs e) { if (IsLoaded) Refresh(); }
    void LibraryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Jobs.SelectedItem is not QueueRow row) return;
        e.Handled = true;
        if (!e.IsRepeat) OpenFile(row);
    }
    void LibraryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not ListBoxItem { DataContext: QueueRow row }) return;
        for (var node = e.OriginalSource as System.Windows.Media.Visual; node != null && node != sender;
             node = VisualTreeHelper.GetParent(node) as System.Windows.Media.Visual)
            if (node is Button) return;
        e.Handled = true;
        OpenFile(row);
    }
    void OpenLibraryItem(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: QueueRow row }) OpenFile(row);
    }
    void OpenFile(QueueRow row)
    {
        if (!row.CanOpen) return;
        try
        {
            if (!File.Exists(row.Job.Destination)) throw new FileNotFoundException("The downloaded file was moved or deleted.", row.Job.Destination);
            launchFile(row.Job.Destination);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Open download"); }
    }
    void Pause(object sender, RoutedEventArgs e)
    {
        if (Jobs.SelectedItem is not QueueRow row) return;
        if (row == active) stop?.Cancel();
        else SetState(row, "Paused");
    }
    void Resume(object sender, RoutedEventArgs e)
    {
        if (Jobs.SelectedItem is QueueRow row && SetState(row, "Queued")) Schedule();
    }
    bool SetState(QueueRow row, string state)
    {
        string old = row.State;
        try { row.Job.State = state; store.Save(row.Job); Refresh(); return true; }
        catch (Exception e) { row.Job.State = old; Refresh(); MessageBox.Show(this, e.Message); return false; }
    }
    void OpenFolder(object sender, RoutedEventArgs e)
    {
        if (Jobs.SelectedItem is not QueueRow row) return;
        try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(row.Job.Destination)!) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message); }
    }
    void Remove(object sender, RoutedEventArgs e)
    {
        if (Jobs.SelectedItem is not QueueRow row || row == active) return;
        if (MessageBox.Show(this, "Remove this queue entry and discard its unfinished bytes? Any finished file stays in its folder.",
            "Remove download", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { store.Remove(row.Job); rows.Remove(row); Refresh(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message); }
    }
    async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (active == null) return;
        e.Cancel = true;
        if (exiting) return;
        exiting = true;
        IsEnabled = false;
        stop?.Cancel();
        if (work != null) await work;
        Close();
    }
}
