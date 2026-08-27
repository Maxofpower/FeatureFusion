namespace FeatureFusion.Dtos
{
	public class UserDto
	{
		public string Id { get; set; } = Guid.NewGuid().ToString();
		public string Role { get; set; } = "User"; // Default role
		public bool HasActiveSubscription { get; set; } = false;
		public bool IsBetaTester { get; set; } = false;

		public UserDto(string role, bool hasSubscription, bool isBetaTester)
		{
			Role = role;
			HasActiveSubscription = hasSubscription;
			IsBetaTester = isBetaTester;
		}
	}

}
