using FeatureFusion.Domain.Entities;
using FeatureFusion.Features.Products.Queries;
using Npgsql.Internal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static FeatureFusion.Infrastructure.CursorPagination.CursorFactory;
using static PaginationHelper;

namespace FeatureFusion.Infrastructure.CursorPagination
{
	public static class CursorFactory
	{
		private static readonly JsonSerializerOptions _options = new()
		{
			PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
			Converters = { new JsonStringEnumConverter() }
		};

		public static string Create<TEntity>(TEntity entity, string sortBy, SortDirection direction,int pageInndex)
		{
			var value = typeof(TEntity).GetProperty(sortBy)?.GetValue(entity);
			var id = (int)typeof(TEntity).GetProperty("Id")?.GetValue(entity);

			var cursor = new CursorData(
				LastValue: value,
				LastId: id,
				SortBy: sortBy,
				Direction: direction,
				pageInndex
				);

			return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cursor, _options)));
		}

		public static CursorData Decode(string encodedCursor)
		{
			if (string.IsNullOrEmpty(encodedCursor)) return null;

			try
			{
				var json   = JsonSerializer.Deserialize<CursorData>(
				Encoding.UTF8.GetString(Convert.FromBase64String(encodedCursor)),
				_options);
				return json;
			}
			catch
			{
				return null;
			}
		}

	}
}
