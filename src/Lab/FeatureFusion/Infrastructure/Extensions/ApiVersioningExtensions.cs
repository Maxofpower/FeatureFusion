using Asp.Versioning;
using Asp.Versioning.Builder;

namespace FeatureFusion.Infrastructure.Extensions;

/// <summary>Single lab API version (1.0) shared by all endpoint groups.</summary>
public static class ApiVersioningExtensions
{
	public static readonly ApiVersion Current = new(1, 0);

	public static ApiVersionSet CreateLabApiVersionSet(this IEndpointRouteBuilder app) =>
		app.NewApiVersionSet()
			.HasApiVersion(Current)
			.ReportApiVersions()
			.Build();
}
