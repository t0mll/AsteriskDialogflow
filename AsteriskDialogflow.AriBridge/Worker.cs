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
        private readonly Bridge _bridge;
        private const string APP_NAME = "bridge_test";

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
            _actionClient = new AriClient(new StasisEndpoint("127.0.0.1", 8088, "asterisk", "asterisk"),
                    "Bridge");

            // Create simple bridge
            _bridge = _actionClient.Bridges.Create("mixing", Guid.NewGuid().ToString(), APP_NAME);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            stoppingToken.ThrowIfCancellationRequested();

            // Hook into required events
            _actionClient.OnStasisStartEvent += c_OnStasisStartEvent;
            _actionClient.OnStasisEndEvent += c_OnStasisEndEvent;
            _actionClient.Connect();

            return Task.CompletedTask;
        }

        private void ActionClientOnConnectionStateChanged(object sender)
        {
            _logger.LogDebug("Connection state is now {0}", _actionClient.Connected);
            _actionClient.Applications.Subscribe(APP_NAME, $"bridge:{_bridge.Id}");
        }

        private void c_OnStasisStartEvent(IAriClient sender, StasisStartEvent e)
        {
            _logger.LogDebug($"Answer channel - {e.Channel.Id}");
            _actionClient.Channels.Answer(e.Channel.Id);

            _logger.LogDebug($"Add channel {e.Channel.Id} to Bridge {_bridge.Id}");
            _actionClient.Bridges.AddChannelAsync(_bridge.Id, e.Channel.Id, "member");
        }

        private void c_OnStasisEndEvent(IAriClient sender, StasisEndEvent e)
        {
            _logger.LogDebug($"Remove channel {e.Channel.Id} from Bridge {_bridge.Id}");
            _actionClient.Bridges.RemoveChannelAsync(_bridge.Id, e.Channel.Id);

            _logger.LogDebug($"Hangup channel - {e.Channel.Id}");
            _actionClient.Channels.Hangup(e.Channel.Id, "normal");
        }
    }
}
