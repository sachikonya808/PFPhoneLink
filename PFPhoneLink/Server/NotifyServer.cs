using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PFPhoneLink.Server
{
    public class NotifyServer : IDisposable
    {
        //Device envelope
        private sealed class Connection
        {
            public required TcpClient Client { get; init; }
            public SemaphoreSlim SendLock { get; } = new SemaphoreSlim(1, 1); //One write at the time per device
        }

        //Storing every connected device
        private readonly ConcurrentDictionary<Connection, byte> _connections= new ConcurrentDictionary<Connection, byte>();
        
        //Tcp server stuff
        private TcpListener? _tcpListener;
        private CancellationTokenSource? _cts;
        private volatile bool _disposed;
        
        //Last message sent
        private byte[]? _lastBytes;

        public bool IsRunning => _tcpListener != null;
        public int ConnectedCount => _connections.Count;
        public bool IsConnected => !_connections.IsEmpty;

        public void Start(int port)
        {
            //Send exception if trying to use object when already disposed
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

            try { cts?.Cancel(); } catch { }
            listener?.Stop();

            foreach (var connection in _connections.Keys)
            {
                RemoveConnection(connection);
            }

            _lastBytes = null;
            cts?.Dispose();
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(token);
                    client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                    var connection = new Connection { Client = client };
                    _connections[connection] = 0;
                    Plugin.Log.Information($"Device Connected! (Current devices {_connections.Count})");

                    //Immediately sync the new device
                    var lastRelay = _lastBytes;
                    if(lastRelay != null ) { _ = SyncDevicesAsync(connection, lastRelay); }
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

            var bytes = Encoding.UTF8.GetBytes(message + "\n");
            _lastBytes = bytes;


            var devices = _connections.Keys.ToArray();
            if (devices.Length == 0) { return; }


            await Task.WhenAll(devices.Select(c => SyncDevicesAsync(c, bytes)));
        }

        private async Task SyncDevicesAsync(Connection connection, byte[] bytes)
        {
            await connection.SendLock.WaitAsync();

            try
            {
                //Wait 5 seconds for the device to respond
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await connection.Client.GetStream().WriteAsync(bytes, timeout.Token);
            }
            catch (Exception ex) when (ex is 
                IOException
                or SocketException
                or ObjectDisposedException
                or InvalidOperationException
                or OperationCanceledException
            ) 
            {
                //Kill if it doesn't respond
                Plugin.Log.Information($"Device dropped: {ex.Message}");
                RemoveConnection(connection);
            }
            finally
            {
                connection.SendLock.Release();
            }
        }

        private void RemoveConnection(Connection connection)
        {
            if(_connections.TryRemove(connection, out _)) { connection.Client.Dispose(); }
        }

        public void Dispose()
        {
            if( _disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
