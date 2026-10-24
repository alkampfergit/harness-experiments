using System.Net;
using System.Text;

namespace Polifemo.Tests.Support;

/// <summary>Minimal in-process HTTP stub to exercise the smoke test's failure paths without a real store.</summary>
public sealed class FakeElasticsearch : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<HttpListenerRequest, (int Status, string Body)> _handler;
    public string Url { get; }

    public FakeElasticsearch(Func<HttpListenerRequest, (int Status, string Body)> handler)
    {
        _handler = handler;
        var port = FreePort();
        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Url + "/");
        _listener.Start();
        _ = Task.Run(Loop);
    }

    public static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task Loop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch (Exception) { return; }
            var (status, body) = _handler(ctx.Request);
            var bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }

    public void Dispose() => _listener.Close();
}
