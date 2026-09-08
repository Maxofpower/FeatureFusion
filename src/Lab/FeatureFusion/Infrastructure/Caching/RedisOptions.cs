using System.ComponentModel.DataAnnotations;

public class RedisSettings
{
	public class RedisOptions
	{
		[Required(ErrorMessage = "Redis connection string is required.")]
		public string ConnectionString { get; set; } = string.Empty;

		public string InstanceName { get; set; } = "MyApp:";
	}
}