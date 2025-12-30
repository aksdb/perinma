using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace perinma.Utils;

public record Result<T>(T? Value, Exception? Error)
{
    public bool IsSuccess => Error == null;
    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(Exception error) => new(default, error);
}

public static class HttpUtil
{
    public static string StartHttpCallbackListener(Action<Result<string>> callback, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var url = $"http://localhost:{port}";
        
        var started = new TaskCompletionSource<bool>();

        _ = Task.Run(async () =>
        {
            started.SetResult(true);
            try
            {
                await using (cancellationToken.Register(() => listener.Stop()))
                {
                    var result = await WaitForHttpCallbackInternalAsync(listener, cancellationToken);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        callback(Result<string>.Success(result));
                    }
                }
            }
            catch (Exception ex)
            {
                callback(Result<string>.Failure(ex));
            }
            finally
            {
                listener.Stop();
            }
        },
            // The task should at least start, no matter what, so we can make sure
            // the callback only needs to be handled in there.
            CancellationToken.None);

        // The task should start no matter what; it will handle the callback, after all.
        started.Task.Wait(CancellationToken.None);
        return url;
    }

    private static async Task<string> WaitForHttpCallbackInternalAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Console.WriteLine($"Listening on port {port}");

            return await Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);

                var stream = client.GetStream();

                // 2. read first line (stop at \r\n)
                var buf = new byte[8192];
                int total = 0, read;
                while ((read = await stream.ReadAsync(buf, total, 1, cancellationToken)) > 0)
                {
                    total += read;
                    if (total >= 2 && buf[total - 2] == 13 && buf[total - 1] == 10)
                        break;
                }

                if (total < 2) return string.Empty;

                // 3. parse query
                var line = Encoding.ASCII.GetString(buf, 0, total - 2);
                var parts = line.Split(' ');
                if (parts.Length < 2) return string.Empty;

                var uri = parts[1]; // "/path?foo=bar"
                var q = uri.IndexOf('?') < 0
                    ? new Dictionary<string, string>()
                    : uri.Substring(uri.IndexOf('?') + 1)
                        .Split('&', StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Split('=', 2))
                        .ToDictionary(
                            kv => Uri.UnescapeDataString(kv[0]),
                            kv => Uri.UnescapeDataString(kv.Length > 1 ? kv[1] : ""));

                // 4. build response
                var html =
                    $"<html><body><pre>{string.Join("\n", q.Select(kv => $"{kv.Key}={kv.Value}"))}</pre></body></html>";
                var bodyBytes = Encoding.UTF8.GetBytes(html);
                var headerBytes = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    $"Content-Length: {bodyBytes.Length}\r\n" +
                    "Connection: close\r\n\r\n");

                await stream.WriteAsync(headerBytes, cancellationToken);
                await stream.WriteAsync(bodyBytes, cancellationToken);
                client.Close();
                return "foobar";
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // That's ok.
            return string.Empty;
        }
        finally
        {
            Console.WriteLine("done");
        }
    }
}
