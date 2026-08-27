namespace FeatureFusion.Infrastructure.Extensions
{
	public static class EndpointRouteBuilderExtensions
	{
		public static RouteHandlerBuilder MapPostWithValidation<TModel>(
			this IEndpointRouteBuilder endpoints,
			string pattern,
			Delegate handler)
		{
			return endpoints.MapPost(pattern, handler)
						   .AddEndpointFilter<ValidationFilter<TModel>>();
		}
	}
}
