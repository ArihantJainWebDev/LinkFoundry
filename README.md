# LinkFoundry

A Windows 10/11 desktop queue for large HTTP and HTTPS downloads. The original console exercise still runs with `dotnet run --project PersonalDownloader.csproj`.

## Start the desktop app

Open `release\PersonalDownloader\PersonalDownloader.Desktop.exe`, or extract the release ZIP and open the executable inside. The framework-dependent build requires the .NET 10 Windows Desktop Runtime matching the app architecture (x64 for the included build). To run from source with the .NET 10 SDK, use `dotnet run --project Desktop/PersonalDownloader.Desktop.csproj`.

Paste a direct link, choose **Add download**, and pick a fresh destination. One queued file runs at a time. Select its row to pause it, resume it, open its folder, or remove its queue entry. Closing the window saves active work for the next launch. Explicitly paused jobs wait for Resume. A failed job retains recoverable parts and can be retried.

## Browser links and installation

Close the app and run `.\Install.ps1` from PowerShell to install for your Windows account, register the link handler, and create a Start menu shortcut. In Chrome or Edge, open the extensions manager, enable developer mode, and choose **Load unpacked**. Select the **Browser** folder printed by the installer. Right-click a download link and choose **Send link to Personal Downloader**. Accept the browser's external-app prompt, then choose the destination in the dashboard. You can also copy a link directly into the app.

Browser handoff transfers the URL alone. Downloads requiring browser cookies, POST requests, blob URLs, expired signed links or private launcher protocols need their original application or a new direct link. Queue records include the original URL, which may contain a private access token. They live under your Windows profile in `%LOCALAPPDATA%\PersonalDownloader\Queue`.

## How recovery works

1. The engine requests byte zero and checks the server's range response, total length, final URL and strong ETag (a version identifier).
2. For a range-capable file with a strong ETag, the engine divides the file into up to four fixed ranges. Each worker writes to its own part inside a job-specific folder alongside the destination.
3. After a pause or connection loss, it probes again and reads the length of each part from disk. Each worker requests the missing bytes with an If-Range condition. A changed representation starts a new generation. A server that ignores ranges receives a fresh whole-file transfer.
4. After verifying lengths, the engine joins the parts and moves the result into place. A pre-existing destination is protected. Multi-part assembly can temporarily require roughly twice the file size in free space. A strong ETag identifies the remote version and does not provide a cryptographic integrity check against a publisher's checksum.

Without validated range support, interrupted transfers restart from byte zero. Unknown-length streams use byte-count progress. Connection failures and timeouts keep retrying with backoff up to 30 seconds between attempts until paused. HTTP 408/429 and server errors retry up to six times. After that, use **Resume / retry** once the server or link is available.

## Networks

The **Transfer room** tab follows the selected queue entry. The room shows the source, routes, saved byte ranges and local drive. The lower bars keep file-offset order as workers download concurrently. During assembly the thin strips track bytes copied into the final file. The **Transfer queue** tab lets you select another job.

Speeds are sampled from bytes written during the current attempt. The moving blocks summarize activity. **Windows route** means automatic routing. Explicitly bound connections show their interface names. Wi-Fi, USB tethering and LAN have separate paths. Unused paths stay dim. Selecting a rack card arms that adapter for new jobs. Automatic routing identifies the adapter from the local socket address when available; unidentifiable traffic is labeled Windows route in the range panel. Previously saved ranges can appear as **On disk** after reopening the app. Unknown-length transfers show bytes until the final size is known. Windows reduced-motion settings disable moving network blocks.

Leave all interfaces unselected for Windows routing, including normal proxy settings. To split large range-capable files across connections, select interfaces before adding the job. Workers open sockets bound to those Windows interfaces. Explicit selection uses direct connections. Offline selections are skipped on the next attempt. If none remain available the engine uses Windows routing. Retries rotate the preferred interface. Multiple interfaces on one upstream link may share the same bandwidth. DNS resolution uses the system resolver.

## Development and checks

| Location | Purpose |
| --- | --- |
| Core | Streaming HTTP transfers, interface binding, queue persistence and recovery |
| Desktop | WPF dashboard, job scheduling and single-instance browser handoff |
| Browser | Chromium context-menu extension |
| Checks | Local HTTP integration tests with forced disconnects |
| DesktopChecks | Windows dashboard interaction checks |
| Program.cs | Original learning prototype |

Run the checks with `dotnet run --project Checks`, `dotnet run --project DesktopChecks`, and `node --test Checks/browser.test.cjs`. The checks use local fixtures. Run `.\Publish.ps1` to build the release folder and ZIP. Rerun the installer after publishing an update. The Windows CI workflow runs on pushes and pull requests when hosted on GitHub with Actions enabled.
