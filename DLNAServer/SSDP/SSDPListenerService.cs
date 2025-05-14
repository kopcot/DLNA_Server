using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Helpers.Logger;
using DLNAServer.Types.IP.Interfaces;
using DLNAServer.Types.UPNP;
using DLNAServer.Types.UPNP.Interfaces;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DLNAServer.SSDP
{
    /// <summary>
    /// Simple Service Discovery Protocol Listener service 
    /// </summary>
    public partial class SSDPListenerService : BackgroundService
    {
        private readonly ILogger<SSDPListenerService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IUPNPDevices _upnpDevices;
        private readonly IIP _ip;
        private readonly ServerConfig _serverConfig;
        private readonly Dictionary<IPEndPoint, UdpClient> _udpClientSenders = [];
        private readonly Encoding decoder = Encoding.UTF8;
        public SSDPListenerService(
            ILogger<SSDPListenerService> logger,
            IServiceScopeFactory serviceScopeFactory,
            ServerConfig serverConfig,
            IUPNPDevices upnpDevices,
            IIP iP)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _serverConfig = serverConfig;
            _upnpDevices = upnpDevices;
            _ip = iP;
        }
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            InformationStarting();
            _udpClientSenders.Clear();
            return StartListeningAsync(stoppingToken);
        }
        public override Task StopAsync(CancellationToken cancellationToken)
        {
            InformationStopping();
            _udpClientSenders.Clear();
            return base.StopAsync(cancellationToken);
        }

        public async Task StartListeningAsync(CancellationToken cancellationToken)
        {
            try
            {
                DebugStartedListening();

                using (var udpClientReceiver = new UdpClient())
                {
                    IPEndPoint localEndPoint = new(IPAddress.Any, _ip.SSDP_PORT);

                    try
                    {
                        // Join the multicast group to listen for M-SEARCH requests
                        udpClientReceiver.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        udpClientReceiver.ExclusiveAddressUse = false;
                        udpClientReceiver.Client.Bind(localEndPoint);
                        udpClientReceiver.Ttl = 10;
                        udpClientReceiver.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 10);
                        udpClientReceiver.JoinMulticastGroup(_ip.MulticastAddress, 10);
                    }
                    catch (Exception)
                    {
                        await SendRestartApplication(cancellationToken);
                        throw;
                    }

                    UdpReceiveResult result;
                    string? receivedMessage;
                    const string headerMSearch = "M-SEARCH";
                    const string headerMan = "MAN: \"ssdp:discover\"";

                    bool isMessageSend = true;

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        result = await udpClientReceiver.ReceiveAsync(cancellationToken);
                        receivedMessage = decoder.GetString(result.Buffer);

                        // Check if it's an M-SEARCH request
                        if (receivedMessage.Contains(headerMSearch, StringComparison.OrdinalIgnoreCase)
                            && receivedMessage.Contains(headerMan, StringComparison.OrdinalIgnoreCase))
                        {
                            isMessageSend &= await HandleSearchRequestAsync(receivedMessage, result.RemoteEndPoint, _upnpDevices.AllUPNPDevices);
                        }

                        if (!isMessageSend)
                        {
                            Random random = new();
                            TimeSpan delay = TimeSpan.FromMinutes(_serverConfig.ServerDelayAfterUnsuccessfulSendSSDPMessageInMin).Add(TimeSpan.FromSeconds(random.Next(60)));

                            WarningStopNotifySend(delay.TotalMinutes);
                            await Task.Delay(delay, CancellationToken.None);

                            await SendRestartApplication(cancellationToken);

                            break;
                        }
                    }

                    DebugListeningCancelationRequest();

                    udpClientReceiver.DropMulticastGroup(_ip.MulticastAddress);
                    CleanUpdClient(udpClientReceiver);
                    foreach (var udpClientSender in _udpClientSenders.Values)
                    {
                        CleanUpdClient(udpClientSender);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                LoggerHelper.LogWarningTaskCanceled(_logger);
            }
            catch (OperationCanceledException)
            {
                LoggerHelper.LogWarningOperationCanceled(_logger);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }

        private async Task<bool> HandleSearchRequestAsync(string message, IPEndPoint remoteEndPoint, UPNPDevice[] upnpDevices)
        {
            var headers = message.Split(Environment.NewLine);
            var searchTarget = headers.FirstOrDefault(static (h) => h.StartsWith("ST:"));
            var devicesEndpoint = upnpDevices.GroupBy(static (d) => d.Endpoint);

            bool isMessageSend = true;

            foreach (var devices in devicesEndpoint)
            {
                if (!_udpClientSenders.TryGetValue(devices.Key, out var udpClient))
                {
                    udpClient = new UdpClient();
                    udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udpClient.ExclusiveAddressUse = false;
                    udpClient.Client.Bind(devices.Key);
                    udpClient.Ttl = 10;
                    udpClient.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 10);

                    _udpClientSenders.Add(devices.Key, udpClient);
                }
                foreach (var device in devices)
                {
                    // Check the "ST" (Search Target) header to determine what the client is looking for
                    if (searchTarget != null &&
                       !searchTarget.Contains(device.Type, StringComparison.CurrentCultureIgnoreCase))
                    {
                        isMessageSend &= await SendMessage(udpClient, device, remoteEndPoint);
                    }

                    if (!isMessageSend)
                    {
                        CleanUpdClient(udpClient);
                        _ = _udpClientSenders.Remove(devices.Key);

                        break;
                    }
                }
                if (!isMessageSend)
                {
                    break;
                }
            }

            return isMessageSend;
        }
        private readonly StringBuilder sb = new(512);
        private readonly ArrayPool<byte> responseBytesPool = ArrayPool<byte>.Shared;
        private async Task<bool> SendMessage(UdpClient udpClient, UPNPDevice device, IPEndPoint remoteEndPoint)
        {
            try
            {
                sb.Clear();

                _ = sb.Append("HTTP/1.1 200 OK\r\n");
                _ = sb.Append("CACHE-CONTROL: max-age=600\r\n");
                _ = sb.Append("DATE: ").Append(DateTime.Now.ToString("R")).Append("\r\n");
                _ = sb.Append("EXT: ").Append("\r\n");
                _ = sb.Append("LOCATION: ").Append(device.Descriptor).Append("\r\n");
                _ = sb.Append("SERVER: ").Append(_serverConfig.DlnaServerSignature).Append("\r\n");
                _ = sb.Append("ST: ").Append(device.Type).Append("\r\n");
                _ = sb.Append("USN: ").Append(device.USN).Append("\r\n");
                _ = sb.Append("\r\n");

                // Convert the response to bytes  
                var message = sb.ToString(); // only one ToString call
                var byteCount = decoder.GetByteCount(message);
                byte[] responseBytes = responseBytesPool.Rent(byteCount);
                try
                {
                    _ = decoder.GetBytes(message, 0, message.Length, responseBytes, 0);

                    // Send SSDP response
                    _ = await udpClient.SendAsync(responseBytes, byteCount, remoteEndPoint);
                }
                finally
                {
                    responseBytesPool.Return(responseBytes, clearArray: false);
                }

                DebugSendResponse(remoteEndPoint.Address, remoteEndPoint.Port, device.Descriptor, device.USN);

                _ = sb.Clear();

                return true;
            }
            catch (SocketException ex)
            {
                ErrorSocketException(ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
                return false;
            }
        }
        private async Task SendRestartApplication(CancellationToken cancellationToken)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                var hostApplicationLifetime = scope.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();

                Stopwatch stopwatch = Stopwatch.StartNew();

                while (!hostApplicationLifetime.ApplicationStarted.IsCancellationRequested &&
                    stopwatch.Elapsed < TimeSpanValues.TimeMin1)
                {
                    await Task.Delay(TimeSpanValues.TimeSecs10, cancellationToken);
                }

                if (hostApplicationLifetime.ApplicationStopped.IsCancellationRequested ||
                    hostApplicationLifetime.ApplicationStopping.IsCancellationRequested)
                {
                    return;
                }

                LoggerHelper.InformationSendingRestart(_logger);

                ServerConfig.DlnaServerRestart = true;
                hostApplicationLifetime.StopApplication();

                await Task.Delay(TimeSpanValues.TimeSecs10, cancellationToken);
            }
        }
        private void CleanUpdClient(UdpClient udpClient)
        {
            try
            {
                if (udpClient.Client.Connected)
                {
                    udpClient.Client.Shutdown(SocketShutdown.Both);
                }

                udpClient.Client.Close();
                udpClient.Close();
                udpClient.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }
    }
}
