using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PersonalDownloader;
using PersonalDownloader.Desktop;

internal static class Program
{
    [STAThread]
    static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exit = 1;
        app.Startup += async (_, _) =>
        {
            try { await Exercise(); exit = 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { app.Shutdown(); }
        };
        app.Run();
        return exit;
    }

    static async Task Eventually(Func<bool> predicate)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(20, limit.Token);
    }

    static async Task Exercise()
    {
        var root = Directory.CreateTempSubdirectory("downloader-ui-").FullName;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/asset";
        var store = new QueueStore(Path.Combine(root, "queue"));
        var job = new Download { Destination = Path.Combine(root, "asset.bin"), Url = url, State = "Paused" };
        store.Save(job);
        string? opened = null;
        var window = new Dashboard(store, path => opened = path) { ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        void Click(string name)
        {
            var button = Control<Button>(name);
            if (!button.IsEnabled) throw new Exception(name + " is disabled");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        try
        {
            window.Show();
            var rack = Control<ListBox>("Networks");
            if (rack.Items.Count > 0)
            {
                rack.SelectedIndex = 0;
                var slot = (RackSlot)rack.SelectedItem;
                var pin = (TextBlock)Control<TransferRoom>("Room").FindName($"Pin{slot.HardwareLane}");
                if (!pin.Text.EndsWith("Selected")) throw new Exception("Selecting a rack device did not arm its 3D path");
                if (!slot.Selection.Contains("Lane A") || !Control<TextBlock>("NetworkSummary").Text.StartsWith("1 of"))
                    throw new Exception("Rack selection was not reflected in the dashboard");
                var asset = Application.GetResourceStream(new Uri(slot.Artwork, UriKind.Relative));
                if (asset == null) throw new Exception("Adapter artwork missing");
                asset.Stream.Dispose();
                rack.SelectedItems.Clear();
                if (!pin.Text.EndsWith("Disconnected")) throw new Exception("Deselected path remained connected");
                if (!Control<TextBlock>("NetworkSummary").Text.Contains("Automatic routing")) throw new Exception("Automatic routing state missing");
                Console.WriteLine("PASS  adapter rack selects a lane, resolves its artwork, and returns to automatic routing");
            }
            window.Receive(url);
            if (Control<TextBox>("Url").Text != url) throw new Exception("URL handoff failed");
            Console.WriteLine("PASS  desktop creates dashboard and accepts browser URL");
            var grid = Control<ListBox>("Jobs");
            grid.SelectedIndex = 0;
            var row = (QueueRow)grid.SelectedItem;
            Click("ResumeButton");
            using var first = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await first.GetStream().WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 4\r\nConnection: close\r\n\r\nt"u8.ToArray());
            var room = Control<TransferRoom>("Room");
            string RoomText(string name) => ((TextBlock)room.FindName(name)).Text;
            await Eventually(() => RoomText("Amount") == "1 B / 4 B");
            if (!RoomText("Pin0").Contains("Disconnected") || !RoomText("Pin1").Contains("Disconnected"))
                throw new Exception("Incorrect 3D route assignment");
            await Eventually(() => row.Speeds.GetValueOrDefault("Windows route") == 0);
            Console.WriteLine("PASS  live room shows measured bytes and a stalled single route");
            Click("PauseButton");
            await Eventually(() => row.State == "Paused");
            if (store.Load(s => throw new Exception(s)).Single().State != "Paused") throw new Exception("Pause was not persisted");
            if (RoomText("Phase") != "Paused" || RoomText("Amount") != "1 B / 4 B") throw new Exception("Room lost paused progress");
            Console.WriteLine("PASS  resume and pause controls persist job state");
            Click("ResumeButton");
            using var second = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var stream = second.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (await reader.ReadLineAsync() is { Length: > 0 }) { }
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 4\r\nConnection: close\r\n\r\ntest"u8.ToArray());
            await Eventually(() => row.State == "Completed" && Control<Button>("RemoveButton").IsEnabled);
            if (File.ReadAllText(job.Destination) != "test" || row.Percent != 100) throw new Exception("Incorrect completed row");
            if (RoomText("Phase") != "Completed" || RoomText("DiskTitle") != "File ready") throw new Exception("Incorrect room completion state");
            Console.WriteLine("PASS  dashboard finishes job and displays completion");
            Control<TabControl>("Views").SelectedIndex = 1;
            window.UpdateLayout();
            var card = (ListBoxItem)grid.ItemContainerGenerator.ContainerFromIndex(0);
            card.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent });
            if (opened != job.Destination) throw new Exception("Library did not open the completed file");
            Console.WriteLine("PASS  library double-click opens the completed download path");
            var twoRoutes = new QueueRow(new Download { Url = url, Destination = job.Destination, State = "Downloading" });
            TransferRange[] ranges = [new(0,24,12,"Wi-Fi"), new(24,24,5,"USB tether"),
                new(48,24,12,"LAN"), new(72,24,5,"USB tether")];
            twoRoutes.Update(new(34,96,"Downloading",ranges));
            foreach (var r in ranges) twoRoutes.Speeds[r.Route] = 1024;
            room.Display(twoRoutes);
            if (RoomText("Pin2") != "LAN · C" || RoomText("Pin0") != "Wi-Fi · A" || RoomText("Pin1") != "USB tether · B + D" ||
                RoomText("Amount") != "34 B / 96 B") throw new Exception("Range assignment disagrees with the file layout");
            twoRoutes.Update(new(34,96,"Retrying in 1s",ranges));
            room.Display(twoRoutes);
            if (RoomText("Phase") != "Reconnecting" || RoomText("Amount") != "34 B / 96 B")
                throw new Exception("Reconnect lost range context");
            Console.WriteLine("PASS  three independent network paths map ranges and retains ranges on retry");
        }
        finally
        {
            window.Close();
            await Eventually(() => !window.IsVisible);
            Directory.Delete(root, true);
        }
    }
}
