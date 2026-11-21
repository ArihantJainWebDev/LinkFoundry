using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Windows;

namespace PersonalDownloader.Desktop;

public partial class App : Application
{
    readonly string channel = "PersonalDownloader-" + WindowsIdentity.GetCurrent().User!.Value;
    Mutex? owner;
    readonly CancellationTokenSource closing = new();
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string link = "";
        if (e.Args.Length > 0)
        {
            const string prefix = "personaldownloader://add/?url=";
            try
            {
                string arg = e.Args[0];
                link = Download.ParseUrl(arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? Uri.UnescapeDataString(arg[prefix.Length..]) : arg).AbsoluteUri;
            }
            catch (ArgumentException ex) { MessageBox.Show(ex.Message); Shutdown(); return; }
        }
        owner = new Mutex(true, @"Local\" + channel, out bool first);
        if (!first)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", channel, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(4000);
                using var writer = new StreamWriter(pipe);
                await writer.WriteLineAsync(link);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
            Shutdown();
            return;
        }
        var window = new Dashboard();
        MainWindow = window;
        window.Show();
        window.Receive(link);
        await Listen(window);
    }
    async Task Listen(Dashboard window)
    {
        while (!closing.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(channel, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(closing.Token);
                using var reader = new StreamReader(pipe);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(closing.Token);
                timeout.CancelAfter(4000);
                string? url = await reader.ReadLineAsync(timeout.Token);
                if (url != null) window.Receive(url);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { await Task.Delay(300); }
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        closing.Cancel();
        owner?.Dispose();
        base.OnExit(e);
    }
}
