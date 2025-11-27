using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ci_Cd.Tests.Integration
{
    public class MockHttpServer : IDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        public string Url { get; }

        public MockHttpServer(string prefix)
        {
            Url = prefix;
            _listener = new HttpListener();
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            Task.Run(() => ListenLoop(_cts.Token));
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    _ = Task.Run(() => Handle(ctx));
                }
                catch (Exception) { }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            // default response
            var bytes = Encoding.UTF8.GetBytes("ok");
            res.StatusCode = 200;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            res.Close();
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            _listener.Close();
        }
    }
}

