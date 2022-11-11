using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MassTransit;
using MassTransit.Saga;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace SagaExample.Tests;

public class SagaTestHelper<TStateMachine, T>
    where TStateMachine : class, SagaStateMachine<T>
    where T : class, SagaStateMachineInstance
{
    private readonly ServiceProvider _provider;

    public ITestHarness Harness { get; }

    public ISagaStateMachineTestHarness<TStateMachine, T> SagaHarness { get; }

    private SagaTestHelper(ServiceProvider provider, ITestHarness harness, ISagaStateMachineTestHarness<TStateMachine, T> sagaHarness)
    {
        _provider = provider;
        Harness = harness;
        SagaHarness = sagaHarness;
    }

    public static SagaTestHelper<TStateMachine, T> Build(Action<IServiceCollection>? registerServices = null, TimeSpan? testTimeout = null)
    {
        var services = new ServiceCollection()
            .AddMassTransitTestHarness(cfg => { cfg.AddSagaStateMachine<TStateMachine, T>(); });
        registerServices?.Invoke(services);
        var provider = services.BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        harness.TestTimeout = testTimeout ?? TimeSpan.FromSeconds(.2);
        var sagaHarness = harness.GetSagaStateMachineHarness<TStateMachine, T>();

        var helper = new SagaTestHelper<TStateMachine, T>(provider, harness, sagaHarness);
        return helper;
    }

    public static async Task Run(
        Func<SagaTestHelper<TStateMachine, T>, Task> testFunc, Action<IServiceCollection>? registerServices = null, TimeSpan? testTimeout = null)
    {
        await Build(registerServices, testTimeout).Run(testFunc);
    }

    public async Task Run(Func<SagaTestHelper<TStateMachine, T>, Task> testFunc)
    {
        await Harness.Start();
        await testFunc(this);
        await _provider.DisposeAsync();
    }

    public T AddSagaInstance(T sagaInstance)
    {
        var dictionary = _provider.GetRequiredService<IndexedSagaDictionary<T>>();
        dictionary.Add(new SagaInstance<T>(sagaInstance));
        return sagaInstance;
    }

    public List<ISagaInstance<T>> AllSagas()
    {
        return SagaHarness.Sagas.Select(_ => true).ToList();
    }
}