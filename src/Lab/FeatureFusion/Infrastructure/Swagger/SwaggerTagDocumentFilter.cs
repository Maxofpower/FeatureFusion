using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FeatureFusion.Infrastructure.Swagger;

/// <summary>Stable Swagger tag order and descriptions for the lab HTTP surface.</summary>
public sealed class SwaggerTagDocumentFilter : IDocumentFilter
{
	private static readonly OpenApiTag[] OrderedTags =
	[
		new() { Name = "Catalog", Description = "Storefront listing and detail: filters by brand/category slug, gallery, specifications." },
		new() { Name = "Products", Description = "Keyset (cursor) catalog paging used by pagination experiments." },
		new() { Name = "Orders", Description = "Lab order commands (idempotency, admission). Not storefront checkout." },
		new() { Name = "Admission", Description = "Deferred capability tickets for order create." },
		new() { Name = "Auth", Description = "JWT login for feature-toggle greeting demos." },
		new() { Name = "Greeting", Description = "Feature-toggle greeting samples." },
		new() { Name = "MediatorDemo", Description = "Mediator pipeline sample (command + query)." },
		new() { Name = "Lab", Description = "Miscellaneous lab endpoints (promotions, validation samples, ping)." }
	];

	public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
	{
		var present = swaggerDoc.Paths
			.SelectMany(p => p.Value.Operations.Values)
			.SelectMany(op => op.Tags ?? [])
			.Select(t => t.Name)
			.Where(n => !string.IsNullOrWhiteSpace(n))
			.ToHashSet(StringComparer.Ordinal);

		swaggerDoc.Tags = OrderedTags.Where(t => present.Contains(t.Name)).ToList();
		foreach (var leftover in present.Except(swaggerDoc.Tags.Select(t => t.Name), StringComparer.Ordinal))
			swaggerDoc.Tags.Add(new OpenApiTag { Name = leftover });
	}
}
