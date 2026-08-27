using FeatureFusion.Services.Authentication;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;


public class AuthService : IAuthService
{
	private readonly IConfiguration _configuration;

	public AuthService(IConfiguration configuration)
	{
		_configuration = configuration;
	}

	public bool ValidateVipUser(string username, string password)
	{
		// Add your VIP user validation logic here (e.g., check against DB)
		return username == "vipuser" && password == "vippassword";
	}

	public string GenerateJwtToken(string username, bool isVip)
	{
		var key = Encoding.ASCII.GetBytes(_configuration["Jwt:Key"]);
		var claims = new List<Claim>
		{
			new Claim(JwtRegisteredClaimNames.Sub, username),
			new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
		};

		if (isVip)
		{
			//just for test purpose !
			claims.Add(new Claim("VIP", "true"));
		}

		var tokenDescriptor = new SecurityTokenDescriptor
		{
			Subject = new ClaimsIdentity(claims),
			Expires = DateTime.UtcNow.AddHours(1),
			SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
			Issuer = _configuration["Jwt:Issuer"],
			Audience = _configuration["Jwt:Audience"]
		};

		var tokenHandler = new JwtSecurityTokenHandler();
		var token = tokenHandler.CreateToken(tokenDescriptor);

		return tokenHandler.WriteToken(token);
	}
}
