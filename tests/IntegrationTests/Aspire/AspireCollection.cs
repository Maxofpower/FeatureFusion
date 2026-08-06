using IntegrationTests.Aspire;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace IntegrationTests.Aspire;

[CollectionDefinition(Name)]
public sealed class AspireCollection : ICollectionFixture<AspireFixture>
{
	public const string Name = "Aspire";
}
