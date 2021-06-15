using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AsterNET.ARI;
using AsterNET.ARI.Models;
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

            // Create client
            SessionsClient sessionsClient = SessionsClient.Create();
            var sessionName = SessionName.FromProjectSession("coffee-shop-stvq", e.Channel.Id).ToString();
            // Initialize streaming call, retrieving the stream object
            SessionsClient.StreamingDetectIntentStream streamingDetectIntent = sessionsClient.StreamingDetectIntent();
            //_actionClient.Channels.ExternalMedia(APP_NAME, "localhost:7777", "slin16");
            // Sending requests and retrieving responses can be arbitrarily interleaved
            // Exact sequence will depend on client/server behavior

            // Define a task to process results from the API
            var responseHandlerTask = Task.Run(async () =>
            {
                var responseStream = streamingDetectIntent.GetResponseStream();
                while (await responseStream.MoveNextAsync())
                {
                    var response = responseStream.Current;
                    var queryResult = response.QueryResult;

                    if (queryResult != null)
                    {
                        _logger.LogDebug($"Query text: {queryResult.QueryText}");
                        if (queryResult.Intent != null)
                        {
                            _logger.LogDebug("Intent detected:");
                            _logger.LogDebug(queryResult.Intent.DisplayName);
                        }
                    }
                }
            });

            // Instructs the speech recognizer how to process the audio content.
            // Note: hard coding audioEncoding, sampleRateHertz for simplicity.
            var queryInput = new QueryInput
            {
                AudioConfig = new InputAudioConfig
                {
                    AudioEncoding = AudioEncoding.Linear16,
                    LanguageCode = "en-US",
                    SampleRateHertz = 8000
                }
            };

            // The first request must **only** contain the audio configuration:
            await streamingDetectIntent.WriteAsync(new StreamingDetectIntentRequest
            {
                QueryInput = queryInput,
                Session = sessionName
            });

            using (FileStream fileStream = new FileStream(@"E:\Documents\Projects\AsteriskDialogflow\AsteriskDialogflow.AriBridge\latte.wav", FileMode.Open))
            {
                // Subsequent requests must **only** contain the audio data.
                // Following messages: audio chunks. We just read the file in
                // fixed-size chunks. In reality you would split the user input
                // by time.
                var buffer = new byte[32 * 1024];
                int bytesRead;
                while ((bytesRead = await fileStream.ReadAsync(
                    buffer, 0, buffer.Length)) > 0)
                {
                    await streamingDetectIntent.WriteAsync(new StreamingDetectIntentRequest
                    {
                        Session = sessionName,
                        InputAudio = ByteString.CopyFrom(buffer, 0, bytesRead)
                    });
                };
            }

            // Tell the service you are done sending data
            await streamingDetectIntent.WriteCompleteAsync();

            // This will complete once all server responses have been processed.
            await responseHandlerTask;
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
