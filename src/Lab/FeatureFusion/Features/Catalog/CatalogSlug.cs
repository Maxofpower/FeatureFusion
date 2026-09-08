using System.Diagnostics.CodeAnalysis;
using BuildingBlocks.Domain;
using FeatureFusion.Domain.Catalog;

namespace FeatureFusion.Features.Catalog;

internal static class CatalogSlug
{
	public static bool TryCreate(string? value, [NotNullWhen(true)] out Slug? slug)
	{
		slug = null;
		if (string.IsNullOrWhiteSpace(value))
			return false;
		try
		{
			slug = Slug.Create(value);
			return true;
		}
		catch (DomainException)
		{
			return false;
		}
	}
}
