using System;
using MassTransit;

namespace SagaExample;

class ExampleState :
    SagaStateMachineInstance
{
    public State CurrentState { get; set; }

    public string MemberNumber { get; set; }

    public Guid? CartTimeoutTokenId { get; set; }

    public int ExpiresAfterSeconds { get; set; }

    public Guid CorrelationId { get; set; }
}


public record CartItemAdded(string MemberNumber);


public interface CartRemoved
{
    string MemberNumber { get; }
}


class CartExpiredTimeoutEvent :
    CartExpiredTimeout
{
    readonly ExampleState _state;

    public CartExpiredTimeoutEvent(ExampleState state)
    {
        _state = state;
    }

    public string MemberNumber => _state.MemberNumber;
}


public interface CartExpiredTimeout
{
    string MemberNumber { get; }
}

public record OrderSubmitted(string MemberNumber);

class MyStateMachine :
    MassTransitStateMachine<ExampleState>
{
    public MyStateMachine()
    {
        Event(() => ItemAdded, x => x.CorrelateBy(p => p.MemberNumber, p => p.Message.MemberNumber)
            .SelectId(context => NewId.NextGuid())
            .InsertOnInitial = true);

        Event(() => Submitted, x => x.CorrelateBy(p => p.MemberNumber, p => p.Message.MemberNumber));

        Schedule(() => CartTimeout, x => x.CartTimeoutTokenId, x =>
        {
            x.Delay = TimeSpan.FromSeconds(10);
            x.Received = p => p.CorrelateBy(state => state.MemberNumber, context => context.Message.MemberNumber);
        });


        Initially(When(ItemAdded)
            .Then(context =>
            {
                context.Instance.MemberNumber = context.Data.MemberNumber;
                context.Instance.ExpiresAfterSeconds = 3;

                LogContext.Debug?.Log("Cart {CartId} Created: {MemberNumber}", context.Instance.CorrelationId, context.Data.MemberNumber);
            })
            .Schedule(CartTimeout, context => context.Init<CartExpiredTimeout>(context.Instance),
                context => TimeSpan.FromSeconds(context.Instance.ExpiresAfterSeconds))
            .TransitionTo(Active));

        During(Active,
            When(CartTimeout.Received)
                .Then(context => LogContext.Debug?.Log("Cart Expired: {MemberNumber}", context.Data.MemberNumber))
                .PublishAsync(context => context.Init<CartRemoved>(context.Instance))
                .Finalize(),
            When(Submitted)
                .Then(context => LogContext.Debug?.Log("Cart Submitted: {MemberNumber}", context.Data.MemberNumber))
                .Unschedule(CartTimeout)
                .PublishAsync(context => context.Init<CartRemoved>(context.Instance))
                .Finalize(),
            When(ItemAdded)
                .Then(context => LogContext.Debug?.Log("Cart Item Added: {MemberNumber}", context.Data.MemberNumber))
                .Schedule(CartTimeout, context => context.Init<CartExpiredTimeout>(context.Instance),
                    context => TimeSpan.FromSeconds(context.Instance.ExpiresAfterSeconds)));

        SetCompletedWhenFinalized();
    }

    public Schedule<ExampleState, CartExpiredTimeout> CartTimeout { get; private set; }

    public Event<CartItemAdded> ItemAdded { get; private set; }
    public Event<OrderSubmitted> Submitted { get; private set; }

    public State Active { get; private set; }
}