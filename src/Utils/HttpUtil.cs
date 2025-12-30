using System;
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace perinma.Utils;

public record Result<T>(T? Value, Exception? Error)
{
    public bool IsSuccess => Error == null;
    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(Exception error) => new(default, error);
}

public static class HttpUtil
{
    public static string StartHttpCallbackListener(Action<Result<NameValueCollection?>> callback,
        CancellationToken cancellationToken)
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
                            callback(Result<NameValueCollection?>.Success(result));
                        }
                    }
                }
                catch (Exception ex)
                {
                    callback(Result<NameValueCollection?>.Failure(ex));
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

    private static async Task<NameValueCollection?> WaitForHttpCallbackInternalAsync(TcpListener listener,
        CancellationToken cancellationToken)
    {
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Console.WriteLine($"Listening on port {port}");

            using var client = await listener.AcceptTcpClientAsync(cancellationToken);

            var stream = client.GetStream();

            // read first line (stop at \r\n)
            var buf = new byte[8192];
            int total = 0, read;
            while ((read = await stream.ReadAsync(buf, total, 1, cancellationToken)) > 0)
            {
                total += read;
                if (total >= 2 && buf[total - 2] == 13 && buf[total - 1] == 10)
                    break;
            }

            if (total < 2) return null;

            // parse query
            var line = Encoding.ASCII.GetString(buf, 0, total - 2);
            var parts = line.Split(' ');
            if (parts.Length < 2) return null;


            var uri = new Uri(new Uri("http://localhost"), parts[1]); // "/path?foo=bar"
            var queryParams = HttpUtility.ParseQueryString(uri.Query);

            // build response
            var html =
                $"<html><body>You can close this window now.</body></html>";
            var bodyBytes = Encoding.UTF8.GetBytes(html);
            var headerBytes = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Connection: close\r\n\r\n");

            await stream.WriteAsync(headerBytes, cancellationToken);
            await stream.WriteAsync(bodyBytes, cancellationToken);
            client.Close();

            return queryParams;
        }
        catch (OperationCanceledException)
        {
            // That's ok.
            return null;
        }
    }
}