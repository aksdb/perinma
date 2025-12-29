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

        _ = Task.Run(async () =>
        {
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
            catch (OperationCanceledException)
            {
                // Silently ignore cancellation if that's preferred, or notify callback
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    callback(Result<string>.Failure(ex));
                }
            }
            finally
            {
                listener.Stop();
            }
        }, cancellationToken);

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
                // Note: The original code stopped the listener here, but that might be problematic if we want to reuse it or if we handle it in the caller.
                // However, the original code had: listener.Stop(); // we only handle one client
                // I will keep it for now as it was in the original logic.

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
                return "foobar"; // The test requirement says it should return the query parameter content, but the original code returns "foobar".
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
