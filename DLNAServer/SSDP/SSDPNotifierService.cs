using DLNAServer.Common;
using DLNAServer.Configuration;
using DLNAServer.Helpers.Logger;
using DLNAServer.Types.IP.Interfaces;
using DLNAServer.Types.UPNP;
using DLNAServer.Types.UPNP.Interfaces;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DLNAServer.SSDP
{
    /// <summary>
    /// Simple Service Discovery Protocol Notification service 
    /// </summary>
    public partial class SSDPNotifierService : BackgroundService
    {
        private readonly ILogger<SSDPNotifierService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IUPNPDevices _upnpDevices;
        private readonly IIP _ip;
        private readonly IPEndPoint _endpoint;
        private readonly ServerConfig _serverConfig;
        private readonly ConcurrentDictionary<(UPNPDevice device, IPEndPoint address, int ssdpPort, string notificationSubtype, string serverSignature), byte[]> messageDataStored = new();
        public SSDPNotifierService(
            ILogger<SSDPNotifierService> logger,
            IServiceScopeFactory serviceScopeFactory,
            IPEndPoint endpoint,
            ServerConfig serverConfig,
            IUPNPDevices upnpDevices,
            IIP iP)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _endpoint = endpoint;
            _serverConfig = serverConfig;
            _upnpDevices = upnpDevices;
            _ip = iP;
        }
        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            InformationStarting(_endpoint);
            messageDataStored.Clear();
            return StartNotifyingAsync(stoppingToken);
        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            InformationStopping();
            messageDataStored.Clear();
            await StopNotifyingAsync();
            await base.StopAsync(cancellationToken);
        }
        public async Task StartNotifyingAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    bool isMessageSend = true;
                    using (UdpClient udpClientSender = new())
                    {
                        try
                        {
                            udpClientSender.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                            udpClientSender.ExclusiveAddressUse = false;
                            udpClientSender.Client.Bind(_endpoint);
                            udpClientSender.Ttl = 10;
                            udpClientSender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 10);
                            udpClientSender.JoinMulticastGroup(_ip.MulticastAddress, 10);
                        }
                        catch
                        {
                            await SendRestartApplication(cancellationToken);
                        }

                        const string notification = "ssdp:alive";

                        while (!cancellationToken.IsCancellationRequested && isMessageSend)
                        {
                            foreach (var device in _upnpDevices.AllUPNPDevices)
                            {
                                isMessageSend &= await SendMessage(udpClientSender, device, _ip.MulticastEndPoint, _ip.SSDP_PORT, notification); // DLNA device discovery
                                isMessageSend &= await SendMessage(udpClientSender, device, _ip.BroadcastEndPoint, _ip.SSDP_PORT, notification); // General announcements 

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

                            await Task.Delay(TimeSpanValues.TimeSecs30, cancellationToken);
                        }

                        udpClientSender.DropMulticastGroup(_ip.MulticastAddress);
                        CleanUpdClient(udpClientSender);
                    }
                }
            }
            catch (TaskCanceledException)
            {
                LoggerHelper.LogWarningTaskCanceled(_logger);
            }
            catch (SocketException ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }

        public async Task StopNotifyingAsync()
        {
            try
            {
                using (UdpClient udpClientSender = new())
                {
                    udpClientSender.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udpClientSender.ExclusiveAddressUse = false;
                    udpClientSender.Client.Bind(_endpoint);
                    udpClientSender.Ttl = 10;
                    udpClientSender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 10);

                    foreach (var device in _upnpDevices.AllUPNPDevices)
                    {
                        _ = await SendMessage(udpClientSender, device, _ip.MulticastEndPoint, _ip.SSDP_PORT, "ssdp:byebye");
                        _ = await SendMessage(udpClientSender, device, _ip.BroadcastEndPoint, _ip.SSDP_PORT, "ssdp:byebye");
                    }

                    CleanUpdClient(udpClientSender);

                    DebugStopNotifySend();
                }
            }
            catch (TaskCanceledException)
            {
                LoggerHelper.LogWarningTaskCanceled(_logger);
            }
            catch (SocketException ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
            catch (Exception ex)
            {
                _logger.LogGeneralErrorMessage(ex);
            }
        }
        private async Task<bool> SendMessage(UdpClient udpClient, UPNPDevice device, IPEndPoint receiverEndPoint, int ssdpPort, string notificationSubtype)
        {
            try
            {
                var messageData = messageDataStored.GetOrAdd((device, receiverEndPoint, ssdpPort, notificationSubtype, _serverConfig.DlnaServerSignature), static (key) =>
                {
                    StringBuilder sb = new();
                    _ = sb.Append("NOTIFY * HTTP/1.1\r\n");
                    _ = sb.Append("HOST: ").Append(key.address.ToString()).Append("\r\n");
                    _ = sb.Append("CACHE-CONTROL: max-age=600\r\n");
                    _ = sb.Append("LOCATION: ").Append(key.device.Descriptor).Append("\r\n");
                    _ = sb.Append("SERVER: ").Append(key.serverSignature).Append("\r\n");
                    _ = sb.Append("NTS: ").Append(key.notificationSubtype).Append("\r\n");
                    _ = sb.Append("NT: ").Append(key.device.Type).Append("\r\n");
                    _ = sb.Append("USN: ").Append(key.device.USN).Append("\r\n");
                    _ = sb.Append("\r\n");

                    return Encoding.UTF8.GetBytes(sb.ToString());
                });

                _ = await udpClient.SendAsync(messageData, messageData.Length, receiverEndPoint);

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
