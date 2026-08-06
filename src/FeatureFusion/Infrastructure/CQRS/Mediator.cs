

using FeatureFusion.Infrastructure.CQRS.Wrapper;
using System.Collections.Concurrent;
using FeatureFusion.Infrastructure.CQRS.Adapter;

namespace FeatureFusion.Infrastructure.CQRS
{
	/// <summary>
	/// Default <see cref="IMediator"/> implementation with cached request/pipeline wrappers.
	/// </summary>
	/// <remarks>
	/// LinkedIn: https://www.linkedin.com/feed/update/urn:li:activity:7311311587372367873/
	/// </remarks>
	public class Mediator : IMediator
	{

		public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken);
		public delegate Task VoidRequestHandlerDelegate(CancellationToken cancellationToken);
		private readonly IServiceProvider _serviceProvider;
		private static readonly ConcurrentDictionary<Type, RequestHandlerWrapper> _requestHandlers = new();
		private static readonly ConcurrentDictionary<Type, PipelineBehaviorWrapper> _behaviorPipelines = new();
		private static readonly ConcurrentDictionary<Type, Func<IRequest, IRequest<Unit>>> _adapterCache = new();

		public Mediator(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

		public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
		{
			if (request == null) throw new ArgumentNullException(nameof(request));

			var requestType = request.GetType();
			var handler = GetRequestHandler<TResponse>(requestType);
			var pipeline = GetBehaviorPipeline<TResponse>(requestType);

			return handler.Handle(request, pipeline, _serviceProvider, cancellationToken);
		}


		public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest
		{
			if (request == null) throw new ArgumentNullException(nameof(request));
			var adapterFactory = _adapterCache.GetOrAdd(request.GetType(), type =>
			{
				var adapterType = typeof(RequestAdapter<>).MakeGenericType(type);
				return cmd => (IRequest<Unit>)Activator.CreateInstance(adapterType, cmd)!;
			});

			return Send(adapterFactory(request), ct);
		
		}

		private RequestHandlerWrapper<TResponse> GetRequestHandler<TResponse>(Type requestType)
		{
			if (_requestHandlers.TryGetValue(requestType, out var cachedHandler))
				return (RequestHandlerWrapper<TResponse>)cachedHandler;

			var wrapperType = typeof(RequestHandlerWrapper<,>).MakeGenericType(requestType, typeof(TResponse));
			var newWrapper = (RequestHandlerWrapper)Activator.CreateInstance(wrapperType)!;

			return (RequestHandlerWrapper<TResponse>)_requestHandlers.GetOrAdd(requestType, newWrapper);
		}

		private PipelineBehaviorWrapper<TResponse> GetBehaviorPipeline<TResponse>(Type requestType)
		{
			if (_behaviorPipelines.TryGetValue(requestType, out var cachedPipeline))
				return (PipelineBehaviorWrapper<TResponse>)cachedPipeline;

			var wrapperType = typeof(PipelineBehaviorWrapper<,>).MakeGenericType(requestType, typeof(TResponse));
			var newWrapper = (PipelineBehaviorWrapper)Activator.CreateInstance(wrapperType)!;

			return (PipelineBehaviorWrapper<TResponse>)_behaviorPipelines.GetOrAdd(requestType, newWrapper);
		}


	}
}