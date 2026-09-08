using BuildingBlocks.Domain;
using FluentAssertions;
using Xunit;

namespace BuildingBlocks.Domain.Tests;

public sealed class ValueObjectTests
{
	[Fact]
	public void Equal_when_components_match()
	{
		var a = new Money(10m, "EUR");
		var b = new Money(10m, "EUR");
		a.Should().Be(b);
		a.GetHashCode().Should().Be(b.GetHashCode());
	}
	[Fact]
	public void Not_equal_when_components_differ()
	{
		new Money(10m, "EUR").Should().NotBe(new Money(11m, "EUR"));
		new Money(10m, "EUR").Should().NotBe(new Money(10m, "USD"));
	}
}

public sealed class IdentityTests
{
	[Fact]
	public void Same_type_and_value_are_equal()
	{
		new SampleId(7).Should().Be(new SampleId(7));
		((int)new SampleId(7)).Should().Be(7);
	}
	[Fact]
	public void Different_identity_types_are_not_equal()
	{
		object a = new SampleId(1);
		object b = new OtherId(1);
		a.Equals(b).Should().BeFalse();
	}
}

public sealed class EntityTests
{
	[Fact]
	public void Same_id_and_type_are_equal()
	{
		var a = new SampleEntity(new SampleId(1));
		var b = new SampleEntity(new SampleId(1));
		a.Should().Be(b);
	}
	[Fact]
	public void CheckRule_throws_when_broken()
	{
		var entity = new SampleEntity(new SampleId(1));
		var act = () => entity.CheckRule(new AlwaysBroken());
		act.Should().Throw<BusinessRuleValidationException>()
			.Which.BrokenRule.Should().BeOfType<AlwaysBroken>();
	}
}

public sealed class AggregateRootTests
{
	[Fact]
	public void Raise_and_clear_domain_events()
	{
		var order = new SampleAggregate(new SampleId(5));
		order.RaiseSomething();
		order.DomainEvents.Should().ContainSingle();
		order.ClearDomainEvents();
		order.DomainEvents.Should().BeEmpty();
	}
}

file sealed class Money : ValueObject
{
	public decimal Amount { get; }
	public string Currency { get; }
	public Money(decimal amount, string currency)
	{
		Amount = amount;
		Currency = currency;
	}
	protected override IEnumerable<object?> GetEqualityComponents()
	{
		yield return Amount;
		yield return Currency;
	}
}

file sealed record SampleId : AggregateId<int>
{
	public SampleId(int value) : base(value) { }
}

file sealed record OtherId : EntityId<int>
{
	public OtherId(int value) : base(value) { }
}

file sealed class SampleEntity : Entity<SampleId>
{
	public SampleEntity(SampleId id) : base(id) { }
}

file sealed class SampleAggregate : AggregateRoot<SampleId>
{
	public SampleAggregate(SampleId id) : base(id) { }
	public void RaiseSomething() => Raise(new SampleHappened());
}

file sealed record SampleHappened : DomainEvent;

file sealed class AlwaysBroken : IBusinessRule
{
	public string Message => "broken";
	public bool IsBroken() => true;
}
