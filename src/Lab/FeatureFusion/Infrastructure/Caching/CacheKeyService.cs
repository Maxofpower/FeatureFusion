using global::FeatureFusion.Domain.Entities;
using global::FeatureFusion.Models;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

// from nopcommerce caching logic
// https://github.com/nopSolutions/nopCommerce
namespace FeatureFusion.Infrastructure.Caching
{
		/// <summary>
		/// Represents the default cache key service implementation
		/// </summary>
		public abstract partial class CacheKeyService
		{
			#region Constants

			/// <summary>
			/// Gets an algorithm used to create the hash value of identifiers need to cache
			/// </summary>
			private string HashAlgorithm => "SHA1";

			#endregion

			#region Fields



			#endregion

			#region Ctor

			protected CacheKeyService()
			{

			}

			#endregion

			#region Utilities

			/// <summary>
			/// Prepare the cache key prefix
			/// </summary>
			/// <param name="prefix">Cache key prefix</param>
			/// <param name="prefixParameters">Parameters to create cache key prefix</param>
			protected virtual string PrepareKeyPrefix(string prefix, params object[] prefixParameters)
			{
				return prefixParameters?.Any() ?? false
					? string.Format(prefix, prefixParameters.Select(CreateCacheKeyParameters).ToArray())
					: prefix;
			}

			/// <summary>
			/// Create the hash value of the passed identifiers
			/// </summary>
			/// <param name="ids">Collection of identifiers</param>
			/// <returns>String hash value</returns>
			protected virtual string CreateIdsHash(IEnumerable<int> ids)
			{
				var identifiers = ids.ToList();

				if (!identifiers.Any())
					return string.Empty;

				var identifiersString = string.Join(", ", identifiers.OrderBy(id => id));
				return HashHelper.CreateHash(Encoding.UTF8.GetBytes(identifiersString), HashAlgorithm);
			}

			/// <summary>
			/// Converts an object to cache parameter
			/// </summary>
			/// <param name="parameter">Object to convert</param>
			/// <returns>Cache parameter</returns>
			protected virtual object CreateCacheKeyParameters(object parameter)
			{
				return parameter switch
				{
					null => "null",
					IEnumerable<int> ids => CreateIdsHash(ids),
					IEnumerable<BaseEntity> entities => CreateIdsHash(entities.Select(entity => entity.Id)),
					BaseEntity entity => entity.Id,
					decimal param => param.ToString(CultureInfo.InvariantCulture),
					_ => parameter
				};
			}

			#endregion

			#region Methods

			/// <summary>
			/// Create a copy of cache key and fills it by passed parameters
			/// </summary>
			/// <param name="cacheKey">Initial cache key</param>
			/// <param name="cacheKeyParameters">Parameters to create cache key</param>
			/// <returns>Cache key</returns>
			public virtual CacheKey PrepareKey(CacheKey cacheKey, params object[] cacheKeyParameters)
			{
				return cacheKey.Create(CreateCacheKeyParameters, cacheKeyParameters);
			}

			/// <summary>
			/// Create a copy of cache key using the default cache time and fills it by passed parameters
			/// </summary>
			/// <param name="cacheKey">Initial cache key</param>
			/// <param name="cacheKeyParameters">Parameters to create cache key</param>
			/// <returns>Cache key</returns>
			public virtual CacheKey PrepareKeyForDefaultCache(CacheKey cacheKey, params object[] cacheKeyParameters)
			{
				var key = cacheKey.Create(CreateCacheKeyParameters, cacheKeyParameters);

				key.CacheTime = 5;

				return key;
			}

			/// <summary>
			/// Create a copy of cache key using the short cache time and fills it by passed parameters
			/// </summary>
			/// <param name="cacheKey">Initial cache key</param>
			/// <param name="cacheKeyParameters">Parameters to create cache key</param>
			/// <returns>Cache key</returns>
			public virtual CacheKey PrepareKeyForShortTermCache(CacheKey cacheKey, params object[] cacheKeyParameters)
			{
				var key = cacheKey.Create(CreateCacheKeyParameters, cacheKeyParameters);

				key.CacheTime = 5;

				return key;
			}

			#endregion


		}
		public partial class HashHelper
		{
			/// <summary>
			/// Create a data hash
			/// </summary>
			/// <param name="data">The data for calculating the hash</param>
			/// <param name="hashAlgorithm">Hash algorithm</param>
			/// <param name="trimByteCount">The number of bytes, which will be used in the hash algorithm; leave 0 to use all array</param>
			/// <returns>Data hash</returns>
			public static string CreateHash(byte[] data, string hashAlgorithm, int trimByteCount = 0)
			{
				if (string.IsNullOrEmpty(hashAlgorithm))
					throw new ArgumentNullException(nameof(hashAlgorithm));

				var algorithm = (HashAlgorithm)CryptoConfig.CreateFromName(hashAlgorithm);
				if (algorithm == null)
					throw new ArgumentException("Unrecognized hash name");

				if (trimByteCount > 0 && data.Length > trimByteCount)
				{
					var newData = new byte[trimByteCount];
					Array.Copy(data, newData, trimByteCount);

					return BitConverter.ToString(algorithm.ComputeHash(newData)).Replace("-", string.Empty);
				}

				return BitConverter.ToString(algorithm.ComputeHash(data)).Replace("-", string.Empty);
			}
		}
}
