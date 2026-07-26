using OrderSystem.Contracts;
using OrderSystem.OrderService.Domain;

namespace OrderSystem.OrderService.Tests.Domain;

public class OrderStateMachineTests
{
    public static IEnumerable<object[]> LegalTransitions =>
    [
        [OrderStatus.Created, OrderStatus.Reserved],
        [OrderStatus.Created, OrderStatus.Cancelled],
        [OrderStatus.Reserved, OrderStatus.Confirmed],
        [OrderStatus.Reserved, OrderStatus.Cancelled],
        [OrderStatus.Confirmed, OrderStatus.Shipped],
        [OrderStatus.Shipped, OrderStatus.Delivered],
    ];

    public static IEnumerable<object[]> IllegalTransitions =>
    [
        [OrderStatus.Created, OrderStatus.Confirmed],
        [OrderStatus.Created, OrderStatus.Shipped],
        [OrderStatus.Created, OrderStatus.Delivered],
        [OrderStatus.Reserved, OrderStatus.Shipped],
        [OrderStatus.Reserved, OrderStatus.Delivered],
        [OrderStatus.Reserved, OrderStatus.Created],
        [OrderStatus.Confirmed, OrderStatus.Delivered],
        [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Delivered, OrderStatus.Cancelled],
        [OrderStatus.Delivered, OrderStatus.RefundPending],
        [OrderStatus.Cancelled, OrderStatus.Created],
    ];

    [Theory]
    [MemberData(nameof(LegalTransitions))]
    public void CanTransition_returns_true_for_every_legal_transition(OrderStatus from, OrderStatus to)
    {
        Assert.True(OrderStateMachine.CanTransition(from, to));
    }

    [Theory]
    [MemberData(nameof(IllegalTransitions))]
    public void CanTransition_returns_false_for_illegal_transitions(OrderStatus from, OrderStatus to)
    {
        Assert.False(OrderStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    public void Terminal_or_no_further_automated_transitions_states_have_no_allowed_transitions(OrderStatus from)
    {
        foreach (OrderStatus to in Enum.GetValues<OrderStatus>())
        {
            if (from == OrderStatus.Shipped && to == OrderStatus.Delivered)
            {
                continue;
            }

            Assert.False(OrderStateMachine.CanTransition(from, to));
        }
    }
}
