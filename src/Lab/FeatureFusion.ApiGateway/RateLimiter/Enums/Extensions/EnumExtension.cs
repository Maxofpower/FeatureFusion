using System.ComponentModel.DataAnnotations;
using System.ComponentModel;
using System.Reflection;

namespace FeatureFusion.ApiGateway.RateLimiter.Enums.Extensions
{
	public static class EnumExtensions
	{
		public static string GetDisplayName(this Enum value)
		{
			return value.GetType()
					   .GetMember(value.ToString())
					   .First()
					   .GetCustomAttribute<DisplayAttribute>()
					   ?.Name ?? value.ToString();
		}
	}

}
