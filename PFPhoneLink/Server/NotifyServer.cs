using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PFPhoneLink.Server
{
    public class NotifyServer : IDisposable
    {
        private TcpListener? _tcpListener;
        private TcpClient? _tcpClient;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private volatile bool _disposed;

        public bool isRunning => _tcpListener != null;
        public bool isConected => _tcpClient?.Connected == true;

        public void Start(int port)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Stop();

            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            var cts = new CancellationTokenSource();

            _tcpListener = listener;
            _cts = cts;

            _ = AcceptLoopAsync(listener, cts.Token);
            Plugin.Log.Information($"TCP Server Started! | Port: {port}");
        }

        public void Stop()
        {
            var cts = Interlocked.Exchange(ref _cts, null);
            var listener = Interlocked.Exchange(ref _tcpListener, null);
            var client = Interlocked.Exchange(ref _tcpClient, null);

            try 
            {
                cts?.Cancel();
            }
            catch { }
            listener?.Stop();
            client?.Dispose();
            cts?.Dispose();
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(token);

                    var old = Interlocked.Exchange(ref _tcpClient, client);
                    old?.Dispose();

                    Plugin.Log.Information("Device Connected!");
                }
            }
            catch(OperationCanceledException)
            {
                Plugin.Log.Information("Server is Shutting Down...");
            }
            catch(ObjectDisposedException)
            {
                Plugin.Log.Information("Listener was stopped");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Accept Loop Crashed!");

                if(ReferenceEquals(_tcpListener, listener))
                {
                    Stop();
                }
            }
        }

        public async Task SendPacketAsync(string message)
        {
            if (_disposed) return;

            var client = _tcpClient;

            if (client is not { Connected: true }) return; 

            await _semaphore.WaitAsync();
            try
            {
                var bytes = Encoding.UTF8.GetBytes(message + "\n");
                await client.GetStream().WriteAsync(bytes);
            }
            catch(Exception ex) when (
                ex is IOException ||
                ex is SocketException ||
                ex is ObjectDisposedException
            )
            {
                client.Dispose();
                Interlocked.CompareExchange(ref _tcpClient, null, client);
                Plugin.Log.Information($"TCP Error: {ex.Message}");
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public void Dispose()
        {
            if( _disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
