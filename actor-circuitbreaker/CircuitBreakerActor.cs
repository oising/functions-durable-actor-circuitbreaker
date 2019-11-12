using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Converters;
using Microsoft.Extensions.Logging;
using Hollan.Function.CircuitLibrary;
using System.Net.Http;
using JetBrains.Annotations;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;

namespace Hollan.Function
{
    [JsonObject(MemberSerialization.OptIn)]
    public class Circuit
    {   
        private readonly ILogger _log;
        private readonly IDurableClient _durableClient;

        public Circuit(IDurableClient client, ILogger log)
        {
            _durableClient = client;
            _log = log;
        }

        [JsonProperty]
        [JsonConverter(typeof(StringEnumConverter))]
        public CircuitState State = CircuitState.Closed;

        // Current rolling window of failures reported for this circuit
        [JsonProperty]
        public IDictionary<string, FailureRequest> FailureWindow = new Dictionary<string, FailureRequest>();

        // The TimeSpan difference from latest to keep failures in the window
        private static readonly TimeSpan WindowSize = TimeSpan.Parse(
            Environment.GetEnvironmentVariable("WindowSize") ?? "00:00:30");

        // The number of failures in the window until opening the circuit
        private static readonly int FailureThreshold = int.Parse(
            Environment.GetEnvironmentVariable("FailureThreshold") ?? "5");

        private static readonly TimeSpan BackOffDuration = TimeSpan.FromSeconds(
            double.Parse(Environment.GetEnvironmentVariable("BackOffDurationSeconds") ?? "300"));

        public void CloseCircuit() => State = CircuitState.Closed;
        
        public void OpenCircuit() => State = CircuitState.Open;

        public async Task AddFailure([NotNull] FailureRequest req)
        {
            if (req == null) throw new ArgumentNullException(nameof(req));

            if(State == CircuitState.Open)
            {
                _log.LogInformation($"Tried to add additional failure to {Entity.Current.EntityKey} that is already open.");
                return;
            }

            FailureWindow.Add(req.RequestId, req);

            var cutoff = req.FailureTime.Subtract(WindowSize);

            // Filter the window only to exceptions within the cutoff timespan
            FailureWindow = FailureWindow.Where(p => p.Value.FailureTime >= cutoff).ToDictionary( p => p.Key, p => p.Value);

            if(FailureWindow.Count >= FailureThreshold)
            {
                _log.LogCritical($"Break this circuit for entity {Entity.Current.EntityKey}!");

                var config = CircuitBreakerOrchestrator.CreateConfiguration(req.InstanceId, BackOffDuration);
                
                await _durableClient.StartNewAsync(nameof(CircuitBreakerOrchestrator.TriggerBreaker), config);

                // Mark the circuit as "open" (circuit is broken)
                State = CircuitState.Open;
            }
            else 
            {
                _log.LogInformation($"The circuit {Entity.Current.EntityKey} currently has {FailureWindow.Count} exceptions in the window of {WindowSize.ToString()}");
            }
        }

        [FunctionName(nameof(Circuit))]
        public static Task Run(
            [EntityTrigger] IDurableEntityContext ctx,
            [DurableClient] IDurableClient client,
            ILogger log) => ctx.DispatchAsync<Circuit>(client, log);
        
    }
}