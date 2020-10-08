using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsterNet.Standard;
using AsterNet.Standard.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AsteriskDialogflow.AriBridge
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly AriClient _actionClient;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
            _actionClient = new AriClient(new StasisEndpoint("127.0.0.1", 8088, "asterisk", "asterisk"),
                    "HelloWorld");
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            stoppingToken.ThrowIfCancellationRequested();

            // Hook into required events
            _actionClient.OnStasisStartEvent += c_OnStasisStartEvent;
            _actionClient.OnChannelDtmfReceivedEvent += ActionClientOnChannelDtmfReceivedEvent;
            _actionClient.OnConnectionStateChanged += ActionClientOnConnectionStateChanged;

            _actionClient.Connect();

            return Task.CompletedTask;
        }

        private void ActionClientOnConnectionStateChanged(object sender)
        {
            _logger.LogDebug("Connection state is now {0}", _actionClient.Connected);
        }

        private void ActionClientOnChannelDtmfReceivedEvent(IAriClient sender, ChannelDtmfReceivedEvent e)
        {
            _logger.LogDebug($"Received DTMF {e.Digit}");

            // When DTMF received
            switch (e.Digit)
            {
                case "*":
                    sender.Channels.Play(e.Channel.Id, "sound:asterisk-friend");
                    break;
                case "#":
                    sender.Channels.Play(e.Channel.Id, "sound:goodbye");
                    sender.Channels.Hangup(e.Channel.Id, "normal");
                    break;
                default:
                    sender.Channels.Play(e.Channel.Id, string.Format("sound:digits/{0}", e.Digit));
                    break;
            }
        }

        private void c_OnStasisStartEvent(IAriClient sender, StasisStartEvent e)
        {
            // Answer the channel
            sender.Channels.Answer(e.Channel.Id);

            // Play an announcement
            sender.Channels.Play(e.Channel.Id, "sound:hello-world");
        }
    }
}
