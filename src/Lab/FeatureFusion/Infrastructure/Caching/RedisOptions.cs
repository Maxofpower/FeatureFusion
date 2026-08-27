using System.ComponentModel.DataAnnotations;

public class RedisSettings
{
	public class RedisOptions
	{
		[Required(ErrorMessage = "Redis connection string is required.")]
		public string ConnectionString { get; set; }

		public string InstanceName { get; set; } = "MyApp:";
	}
}