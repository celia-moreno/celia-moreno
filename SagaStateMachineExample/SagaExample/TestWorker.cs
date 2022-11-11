using System;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Hosting;

namespace SagaExample;

public class TestWorker :
    BackgroundService
{
    private readonly IBus _publishEndpoint;

    public TestWorker(IBus publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Console.WriteLine("Sending Request for cart Item added");

            var request1 = new CartItemAdded("Member1");

            
            await _publishEndpoint.PublishBatch(new[] { request1 }, stoppingToken);

            await Task.Delay(1000, stoppingToken);
            
            var request2 = new OrderSubmitted("Member1");

            
            await _publishEndpoint.PublishBatch(new[] { request2 }, stoppingToken);

            await Task.Delay(100000000, stoppingToken);
        }
    }
}