namespace FeatureFusion.Services.Authentication
{
	public interface IAuthService
	{
		bool ValidateVipUser(string username, string password);
		string GenerateJwtToken(string username, bool isVip);
	}

}
