using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SagaExample;

public static class Program
{
    public static async Task Main(string[] args)
    {
        await CreateHostBuilder(args).Build().RunAsync();
    }
    
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((hostContext, services) =>
            {
                services.AddMassTransit(config =>
                {
                    config.SetKebabCaseEndpointNameFormatter();

                    config.AddServiceBusMessageScheduler();
                    config.AddSagaStateMachine<MyStateMachine, ExampleState>()
                        .InMemoryRepository();
            
                    config.UsingAzureServiceBus((context, cfg) =>
                    {
                        cfg.Host("Put here your service bus instance");
                        cfg.UseInMemoryOutbox();
                    
                        cfg.UseServiceBusMessageScheduler();
                        cfg.ConfigureEndpoints(context);
                    });
                });

                services.AddHostedService<TestWorker>();
            });
}