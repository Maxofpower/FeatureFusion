using FeatureFusion.Models;
using static FeatureFusion.Infrastructure.CQRS.Mediator;

namespace FeatureFusion.Infrastructure.CQRS
{
	/// <summary>
	/// Manual CQRS mediator (Send + pipeline behaviors). Void requests are adapted via <see cref="Adapter.RequestAdapter{TRequest}"/>.
	/// </summary>
	/// <remarks>
	/// LinkedIn: Manual Mediator + pipeline behaviors —
	/// https://www.linkedin.com/feed/update/urn:li:activity:7311311587372367873/
	/// Catalog: docs/linkedin-posts.md (<c>mediator</c>).
	/// </remarks>
	public interface IMediator
	{
		/// <summary>Sends a request that returns <typeparamref name="TResponse"/>.</summary>
		Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

		/// <summary>Sends a void request (no response).</summary>
		Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
			where TRequest : IRequest;
	}

	/// <summary>Marker for requests that produce a response.</summary>
	public interface IRequest<out TResponse> { }

	/// <summary>Marker for void requests.</summary>
	public interface IRequest { }

	/// <summary>Handles <see cref="IRequest{TResponse}"/>.</summary>
	public interface IRequestHandler<in TRequest, TResponse>
		where TRequest : IRequest<TResponse>
	{
		Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
	}

	/// <summary>Handles void <see cref="IRequest"/>.</summary>
	public interface IRequestHandler<in TRequest>
		where TRequest : IRequest
	{
		Task Handle(TRequest request, CancellationToken cancellationToken);
	}

	/// <summary>Cross-cutting pipeline behavior around a request/response handler.</summary>
	public interface IPipelineBehavior<in TRequest, TResponse>
	{
		Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken = default);
	}

	/// <summary>Cross-cutting pipeline behavior around a void request handler.</summary>
	public interface IPipelineBehavior<TRequest>
	{
		Task Handle(TRequest request, VoidRequestHandlerDelegate next, CancellationToken cancellationToken = default);
	}
}
