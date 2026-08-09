using BuildingBlocks.Mediator;

namespace FeatureFusion.Features.MediatorDemo.Queries;

/// <summary>Infra-free query for Swagger / Aspire Mediator smoke tests.</summary>
public sealed class GetEchoStatusQuery : IQuery<Result<EchoStatusResponse>>
{
}

public sealed record EchoStatusResponse(string Status, string ActivitySource, string Hint);

public sealed class GetEchoStatusQueryHandler : IQueryHandler<GetEchoStatusQuery, Result<EchoStatusResponse>>
{
	public Task<Result<EchoStatusResponse>> Handle(
		GetEchoStatusQuery request,
		CancellationToken cancellationToken)
	{
		var response = new EchoStatusResponse(
			Status: "ready",
			ActivitySource: "BuildingBlocks.Mediator",
			Hint: "In Aspire Dashboard, filter traces by source BuildingBlocks.Mediator after calling POST /api/v2/mediator-demo/echo.");

		return Task.FromResult(Result<EchoStatusResponse>.Success(response));
	}
}
