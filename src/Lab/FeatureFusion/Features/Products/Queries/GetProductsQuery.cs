using FeatureFusion.Dtos;
using BuildingBlocks.Mcp;
using BuildingBlocks.Mediator;
using FeatureFusion.Infrastructure.CursorPagination;
using Swashbuckle.AspNetCore.Annotations;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FeatureFusion.Features.Products.Queries
{
	[McpTool("products.list", Description = "List the PostgreSQL product catalog with keyset (cursor) pagination")]
	public sealed record GetProductsQuery : IQuery<Result<PagedResult<ProductDto>>>
	{
		[SwaggerParameter(Description = "Page size (1–100). Default 20.")]
		[Range(1, 100)]
		public int Limit { get; init; } = 20;

		[SwaggerParameter(Required = false, Description = "Opaque cursor from a previous response (NextCursor or PreviousCursor). Pass it back unchanged. Empty = first page, or last page when pageDirection is Backward.")]
		public string Cursor { get; init; } = string.Empty;

		[SwaggerParameter(Description = "Sort field: Id, Name, Price, CreatedAt, or NameThenPrice. Composite keys always include unique Id as the tie-breaker.")]
		public ProductSortField SortBy { get; init; } = ProductSortField.Id;

		[SwaggerParameter(Description = "Sort direction: Ascending or Descending.")]
		public SortDirection SortDirection { get; init; } = SortDirection.Ascending;

		[SwaggerParameter(Required = false, Description = "Used when Cursor is empty. Forward (default) = first page. Backward = last page. Ignored when a cursor is present (walk is encoded in the cursor).")]
		public PageDirection PageDirection { get; init; } = PageDirection.Forward;
	}

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public enum ProductSortField
	{
		Id,
		Name,
		Price,
		CreatedAt,
		/// <summary>Composite: Name, then Price, then unique Id (3-column keyset showcase).</summary>
		NameThenPrice
	}

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public enum SortDirection
	{
		Ascending,
		Descending
	}

	[JsonConverter(typeof(JsonStringEnumConverter))]
	public enum PageDirection
	{
		Forward,
		Backward
	}
}
