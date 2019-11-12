using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Hollan.Function
{
    [UsedImplicitly]
    public class CircuitBreakerOrchestrator
    {
        private const string ExternalEventForceCloseCircuit = "CloseCircuit";

        private readonly ILogger<CircuitBreakerOrchestrator> _logger;

        public CircuitBreakerOrchestrator(ILogger<CircuitBreakerOrchestrator> logger)
        {
            _logger = logger;
        }

        public static CircuitConfiguration CreateConfiguration([NotNull] string instanceId, TimeSpan backOffDuration)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(instanceId));

            return (instanceId, backOffDuration);
        }

        [FunctionName(nameof(TriggerBreaker))]
        public async Task TriggerBreaker(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            if (!context.IsReplaying) _logger.LogInformation("Disabling function app to open circuit");

            var config = context.GetInput<CircuitConfiguration>();

            async Task<DurableHttpResponse> FunctionCommand(string operation)
            {
                var functionCommandRequest = new DurableHttpRequest(
                    HttpMethod.Post,
                    new Uri($"https://management.azure.com{config.ResourceId}/{operation}?api-version=2016-08-01"),
                    tokenSource: new ManagedIdentityTokenSource("https://management.core.windows.net"));

                return await context.CallHttpAsync(functionCommandRequest);
            }

            var response = await FunctionCommand("stop");

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new ArgumentException($"Failed to stop Function App: {response.StatusCode}: {response.Content}");
            }

            var timerStarted = context.CurrentUtcDateTime;

            using (var cancelSource = new CancellationTokenSource())
            {
                var timerTask = context.CreateTimer(timerStarted.Add(config.BackOffDuration),
                    cancelSource.Token);

                var externalEventTask = context.WaitForExternalEvent(
                    CircuitBreakerOrchestrator.ExternalEventForceCloseCircuit);

                _ = Task.WhenAny(timerTask, externalEventTask);
            }

            response = await FunctionCommand("start");

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new ArgumentException($"Failed to start Function App: {response.StatusCode}: {response.Content}");
            }

            if (!context.IsReplaying) _logger.LogInformation("Function disabled");
        }
    }
}