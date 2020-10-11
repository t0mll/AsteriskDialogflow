using System;
using System.Threading;
using System.Threading.Tasks;
using AsterNet.Standard;
using AsterNet.Standard.Models;
using Google.Api.Gax.Grpc;
using Google.Cloud.Dialogflow.V2;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
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
            _actionClient.OnConnectionStateChanged += ActionClientOnConnectionStateChanged;
            _actionClient.OnStasisStartEvent += c_OnStasisStartEvent;
            _actionClient.OnStasisEndEvent += c_OnStasisEndEvent;
            _actionClient.Connect();

            return Task.CompletedTask;
        }

        private void ActionClientOnConnectionStateChanged(object sender)
        {
            _logger.LogDebug("Connection state is now {0}", _actionClient.Connected);

            //if (_actionClient.Connected)
                //_actionClient.Applications.Subscribe(APP_NAME, $"bridge:{_bridge.Id}");
        }

        private async void c_OnStasisStartEvent(IAriClient sender, StasisStartEvent e)
        {
            _logger.LogDebug($"Answer channel - {e.Channel.Id}");
            _actionClient.Channels.Answer(e.Channel.Id);

            _logger.LogDebug($"Add channel {e.Channel.Id} to Bridge {_bridge.Id}");
            await _actionClient.Bridges.AddChannelAsync(_bridge.Id, e.Channel.Id, "member");

            // // Create client
            // SessionsClient sessionsClient = SessionsClient.Create();
            // // Initialize streaming call, retrieving the stream object
            // SessionsClient.StreamingDetectIntentStream response = sessionsClient.StreamingDetectIntent();

            // // Sending requests and retrieving responses can be arbitrarily interleaved
            // // Exact sequence will depend on client/server behavior

            // // Create task to do something with responses from server
            // Task responseHandlerTask = Task.Run(async () =>
            // {
            //     // Note that C# 8 code can use await foreach
            //     AsyncResponseStream<StreamingDetectIntentResponse> responseStream = response.GetResponseStream();
            //     while (await responseStream.MoveNextAsync())
            //     {
            //         StreamingDetectIntentResponse responseItem = responseStream.Current;
            //         // Do something with streamed response
            //     }
            //     // The response stream has completed
            // });

            // // Send requests to the server
            // bool done = false;
            // while (!done)
            // {
            //     // Initialize a request
            //     StreamingDetectIntentRequest request = new StreamingDetectIntentRequest
            //     {
            //         SessionAsSessionName = SessionName.FromProjectSession("coffee-shop-stvq", e.Channel.Id),
            //         QueryParams = new QueryParameters(),
            //         QueryInput = new QueryInput(),
            //         OutputAudioConfig = new OutputAudioConfig(),
            //         InputAudio = ByteString.Empty,
            //         OutputAudioConfigMask = new FieldMask(),
            //     };
            //     // Stream a request to the server
            //     await response.WriteAsync(request);
            //     // Set "done" to true when sending requests is complete
            // }

            // // Complete writing requests to the stream
            // await response.WriteCompleteAsync();
            // // Await the response handler
            // // This will complete once all server responses have been processed
            // await responseHandlerTask;
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
