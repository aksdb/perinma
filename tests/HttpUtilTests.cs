using System.Net;
using System.Net.Sockets;
using System.Text;
using perinma.Utils;

namespace tests;

public class HttpUtilTests
{
    [Test]
    public async Task TestReturnsQueryParameterContent()
    {
        using var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<string>();
        
        var url = HttpUtil.StartHttpCallbackListener(result =>
        {
            if (result.IsSuccess) tcs.SetResult(result.Value!);
            else tcs.SetException(result.Error!);
        }, cts.Token);

        var uri = new Uri(url);
        using var client = new TcpClient();
        await client.ConnectAsync(uri.Host, uri.Port);
        var stream = client.GetStream();
        const string request = "GET /?code=secret_token HTTP/1.1\r\nHost: localhost\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request));

        var result = await tcs.Task;
        
        Assert.That(result, Is.EqualTo("secret_token"));
    }

    [Test]
    public async Task TestCanBeCancelled()
    {
        using var cts = new CancellationTokenSource();
        
        var url = HttpUtil.StartHttpCallbackListener(result =>
        {
        }, cts.Token);
        
        await cts.CancelAsync();

        var uri = new Uri(url);
        
        // The listener might take a moment to stop.
        bool stopped = false;
        for (int i = 0; i < 10; i++)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(uri.Host, uri.Port);
            }
            catch (SocketException)
            {
                stopped = true;
                break;
            }
            await Task.Delay(50);
        }
        
        Assert.That(stopped, Is.True, "Listener should have stopped after cancellation");
    }

    [Test]
    public async Task TestHandlesNonHttpRequestsGracefully()
    {
        using var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<string>();
        
        var url = HttpUtil.StartHttpCallbackListener(result =>
        {
            if (result.IsSuccess) tcs.SetResult(result.Value!);
            else tcs.SetException(result.Error!);
        }, cts.Token);

        var uri = new Uri(url);
        using var client = new TcpClient();
        await client.ConnectAsync(uri.Host, uri.Port);
        var stream = client.GetStream();
        await stream.WriteAsync(new byte[] { 0, 1, 2, 3, 4, 5, 13, 10 }); // Not a valid HTTP GET

        var result = await tcs.Task;
        
        Assert.That(result, Is.EqualTo(string.Empty));
    }
}
