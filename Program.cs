using System.Net.Http;

Console.WriteLine("LinkFoundry");

while (true)
{
    Console.Write("Enter the URL: ");

    string? downloadURL = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(downloadURL))
    {
        Console.WriteLine("You haven't entered a URL!");

    }
    else if (Uri.TryCreate(downloadURL, UriKind.Absolute, out Uri? parsedURL) && ((parsedURL.Scheme == "http") || (parsedURL.Scheme == "https")))
    {
        Console.Write("Parsed URL: ");
        Console.WriteLine(parsedURL);

        Console.WriteLine($"Protocol: {parsedURL.Scheme}");
        Console.WriteLine($"Server: {parsedURL.Host}");
        Console.WriteLine($"Path: {parsedURL.AbsolutePath}");

        try
        {
            using HttpClient client = new HttpClient();

            Console.WriteLine("Connecting to the server...");

            using HttpResponseMessage response = await client.GetAsync(parsedURL, HttpCompletionOption.ResponseHeadersRead);

            Console.WriteLine($"Status Code: {(int)response.StatusCode}");
            Console.WriteLine($"Status name: {response.StatusCode}");
            Console.WriteLine($"Content type: {response.Content.Headers.ContentType}");
            Console.WriteLine($"Content length: {response.Content.Headers.ContentLength}");

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine("The server accepted the request.");
                Console.Write("Save as (example: my-page.html): ");
                string? fileName = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(fileName)
                    || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || fileName == "."
                    || fileName == "..")
                {
                    Console.WriteLine("Enter a valid filename without a folder path.");
                    continue;
                }

                string savePath = Path.Combine(
                    Environment.CurrentDirectory,
                    fileName
                );

                string partialPath = savePath + ".partial";

                if (File.Exists(savePath))
                {
                    Console.WriteLine("The destination file already exists.");
                    continue;
                }

                using Stream networkStream = await response.Content.ReadAsStreamAsync();

                using FileStream fileStream = new FileStream(
                    partialPath,
                    FileMode.CreateNew,
                    FileAccess.Write
                );

                byte[] buffer = new byte[8192];
                long totalBytesDownloaded = 0;
                long? totalBytes = response.Content.Headers.ContentLength;

                while (true)
                {
                    int bytesRead = await networkStream.ReadAsync(buffer.AsMemory());

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));

                    totalBytesDownloaded += bytesRead;

                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        double percentage =
                            (double)totalBytesDownloaded / totalBytes.Value * 100;

                        Console.Write(
                            $"\rDownloaded: {totalBytesDownloaded} / {totalBytes.Value} bytes ({percentage:F1}%)"
                        );
                    }
                    else
                    {
                        Console.Write($"\rDownloaded: {totalBytesDownloaded} bytes");
                    }
                }

                Console.WriteLine();

                if (totalBytes.HasValue && totalBytesDownloaded != totalBytes.Value)
                {
                    throw new IOException("The downloaded size does not match the expected size.");
                }

                await fileStream.DisposeAsync();

                File.Move(partialPath, savePath);

                Console.WriteLine($"Saved to: {savePath}");
            }
            else
            {
                Console.WriteLine("The server responded, but the request was unsuccessful.");
            }

        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"The Request couldn't be completed: {e.Message}");
        }
        catch (IOException e)
        {
            Console.WriteLine($"Could not save the complete file: {e.Message}");
        }

        break;

    }
    else
    {
        Console.WriteLine("Enter a complete URL starting with either http or https!");
    }
}