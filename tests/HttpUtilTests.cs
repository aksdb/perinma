using System.Collections.Specialized;
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
        var tcs = new TaskCompletionSource<NameValueCollection?>();

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
        Assert.That(result, Is.Not.Null);

        Assert.That(result, Is.EquivalentTo(new NameValueCollection
        {
            { "code", "secret_token" }
        }));
    }

    [Test]
    public async Task TestCanBeCancelled()
    {
        using var cts = new CancellationTokenSource();
        var completionSource = new TaskCompletionSource<bool>();

        var url = HttpUtil.StartHttpCallbackListener(result => { completionSource.SetResult(result.IsSuccess); },
            cts.Token);

        await cts.CancelAsync();
        await completionSource.Task;

        var uri = new Uri(url);

        var stopped = false;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(uri.Host, uri.Port);
        }
        catch (SocketException)
        {
            stopped = true;
        }

        Assert.That(stopped, Is.True, "Listener should have stopped after cancellation");
    }

    [Test]
    public async Task TestHandlesNonHttpRequestsGracefully()
    {
        using var cts = new CancellationTokenSource();
        var tcs = new TaskCompletionSource<NameValueCollection?>();

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

        Assert.That(result, Is.Null);
    }
}