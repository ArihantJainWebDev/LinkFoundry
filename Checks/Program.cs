using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PersonalDownloader;

var data = new byte[9 * 1024 * 1024 + 7];
new Random(123).NextBytes(data);
var replacement = data.Select(x => (byte)(x ^ 0xff)).ToArray();
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
await using var server = builder.Build();
var ranges = new ConcurrentQueue<(string Mode, long Start, long End)>();
int dropped = 0;
bool changed = false;
server.MapGet("/{mode}", async (HttpContext ctx, string mode) =>
{
    var r = ctx.Response;
    var bytes = mode == "change" && changed ? replacement : data;
    string tag = bytes == data ? "\"one\"" : "\"two\"";
    if (mode == "missing") { r.StatusCode = 404; return; }
    if (mode == "empty") { r.ContentLength = 0; return; }
    if (mode != "untagged") r.Headers.ETag = mode == "weak" ? "W/" + tag : tag;
    if (System.Net.Http.Headers.RangeHeaderValue.TryParse(ctx.Request.Headers.Range, out var range)
        && mode is not ("whole" or "unknown"))
    {
        long start = range.Ranges.Single().From!.Value, end = range.Ranges.Single().To!.Value;
        ranges.Enqueue((mode, start, end));
        if (mode != "ignore" || end == 0)
        {
            if (end != 0 && ctx.Request.Headers.IfRange != tag) throw new Exception("Missing or incorrect If-Range.");
            r.StatusCode = 206;
            long claimedStart = mode == "malformed" && end != 0 ? start + 1 : start;
            r.Headers.ContentRange = $"bytes {claimedStart}-{end}/{bytes.Length}";
            r.ContentLength = end - start + 1;
            if (mode == "drop" && end > 0 && Interlocked.Exchange(ref dropped, 1) == 0)
            {
                await r.Body.WriteAsync(bytes.AsMemory((int)start, 131072));
                await r.Body.FlushAsync();
                await Task.Delay(100);
                ctx.Abort();
                return;
            }
            await r.Body.WriteAsync(bytes.AsMemory((int)start, (int)(end - start + 1)));
            return;
        }
    }
    if (mode != "unknown") r.ContentLength = bytes.Length;
    await r.Body.WriteAsync(bytes);
});
server.MapMethods("/version/6", ["HEAD","GET"], () => Results.Redirect("/files/CursorSetup.exe"));
server.MapMethods("/files/CursorSetup.exe", ["HEAD","GET"], () => Results.Ok());
server.MapMethods("/metadata/6", ["HEAD","GET"], (HttpContext c) =>
{
    if (c.Request.Method == "HEAD") return Results.StatusCode(405);
    c.Response.Headers.ContentDisposition = "attachment; filename=legacy.exe; filename*=UTF-8''Cursor%20Setup.exe";
    return Results.Ok();
});
await server.StartAsync();
var root = Directory.CreateTempSubdirectory("personal-downloader-checks-").FullName;
try
{
    var store = new QueueStore(Path.Combine(root, "queue"));
    var transfer = new Transfer(store);
    Download New(string mode) => new() { Url = server.Urls.Single() + "/" + mode, Destination = Path.Combine(root, mode) };
    void Check(bool result, string label)
    {
        if (!result) throw new Exception(label);
        Console.WriteLine("PASS  " + label);
    }
    using (var client = new HttpClient())
    {
        var origin = new Uri(server.Urls.Single());
        Check(await DownloadName.ResolveAsync(client,new Uri(origin,"/version/6"),default) == "CursorSetup.exe",
            "numeric installer URL uses the redirected executable name");
        Check(await DownloadName.ResolveAsync(client,new Uri(origin,"/metadata/6"),default) == "Cursor Setup.exe",
            "unsupported HEAD falls back to GET and prefers encoded attachment filename");
    }
    foreach (string mode in new[] { "whole", "unknown", "empty", "untagged", "weak", "ignore", "malformed", "drop", "pause", "change" })
    {
        var job = New(mode);
        if (mode is "pause" or "change")
        {
            using var stop = new CancellationTokenSource();
            try { await transfer.RunAsync(job, p => { if (p.Bytes >= 65536) stop.Cancel(); }, stop.Token); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            Check(!File.Exists(job.Destination) && Directory.GetFiles(job.WorkDirectory(), "*.part").Any(p => new FileInfo(p).Length > 0),
                mode + " preserves unfinished bytes");
            job = store.Load(s => throw new Exception(s)).Single(j => j.Id == job.Id);
            changed = mode == "change";
        }
        TransferRange[]? finalRanges = null;
        bool sawRetry = false;
        await transfer.RunAsync(job, p =>
        {
            finalRanges = p.Ranges ?? throw new Exception("Lost range data during transfer");
            if (p.Bytes != finalRanges.Sum(r => r.Saved)) throw new Exception("Total progress disagrees with range counters");
            if (p.Activity.StartsWith("Retrying")) sawRetry = finalRanges.Any(r => r.Saved > 0);
        }, CancellationToken.None);
        var expected = mode == "empty" ? [] : changed ? replacement : data;
        Check(File.ReadAllBytes(job.Destination).SequenceEqual(expected) && !Directory.Exists(job.WorkDirectory()), mode + " exact output");
        if (mode == "drop")
            Check(sawRetry && finalRanges != null && finalRanges.Sum(r => r.Assembled) == data.Length &&
                finalRanges.Sum(r => r.Existing) > 0, "retry retains saved bytes and merge accounts for every range");
        if (mode == "unknown")
            Check(finalRanges is [{ Length: null, Route: "Windows route" }] && finalRanges[0].Saved == data.Length,
                "unknown-length stream reports actual bytes on the system route");
        if (mode is "pause" or "drop")
            Check(ranges.Any(r => r.Mode == mode && r.Start > 0 && r.Start % (data.Length / 3) != 0),
                mode + " requests remaining bytes");
    }
    var collision = New("whole");
    try { await transfer.RunAsync(collision, _ => { }, default); throw new Exception("Overwrite allowed"); }
    catch (IOException) { Check(File.ReadAllBytes(collision.Destination).SequenceEqual(data), "existing destination protected"); }
    var missing = New("missing");
    try { await transfer.RunAsync(missing, _ => { }, default); throw new Exception("404 accepted"); }
    catch (HttpRequestException e) { Check(e.StatusCode == HttpStatusCode.NotFound && !File.Exists(missing.Destination), "404 creates no final file"); }
    foreach (bool afterMove in new[] { false, true })
    {
        var job = New("finalize-" + afterMove);
        job.State = "Finalizing";
        job.ReadyFile = "assembled.tmp";
        job.Size = 3;
        Directory.CreateDirectory(job.WorkDirectory());
        string ready = Path.Combine(job.WorkDirectory(), job.ReadyFile);
        File.WriteAllBytes(afterMove ? job.Destination : ready, [1, 2, 3]);
        store.Save(job);
        Transfer.RecoverFinalization(job, store);
        Check(job.State == "Completed" && File.ReadAllBytes(job.Destination).SequenceEqual(new byte[] { 1, 2, 3 }),
            $"restart {(afterMove ? "after" : "before")} final rename");
    }
    if (args.Contains("--check-adapters"))
    {
        foreach (var adapter in Connections.List())
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces().First(n => n.Id == adapter.Id);
            var local = nic.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            if (local == null) continue;
            using var listener = new TcpListener(local, 0);
            listener.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var client = Connections.Create(adapter.Id);
            var get = client.GetStringAsync($"http://{listener.LocalEndpoint}/", timeout.Token);
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            using var stream = peer.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 }) { }
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(), timeout.Token);
            Check(await get == "ok" && ((IPEndPoint)peer.Client.RemoteEndPoint!).Address.Equals(local),
                adapter.Name + " uses the selected source address for a local HTTP connection");
        }
    }
}
finally
{
    await server.StopAsync();
    Directory.Delete(root, true);
}
