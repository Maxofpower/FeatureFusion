using FeatureFusion.Dtos;
using FeatureFusion.Services.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace FeatureFusion.Controllers.V2
{
	//[ApiVersion("2.0")]
	[Route("api/v{version:apiVersion}/[controller]")]
	[ApiController]
	public class AuthController : ControllerBase
	{
		private readonly IAuthService _authService;

		public AuthController(IAuthService authService)
		{
			_authService = authService;
		}

		[HttpPost("login")]
		public IActionResult Login([FromBody] LoginDto login)
		{
			if (_authService.ValidateVipUser(login.Username, login.Password))
			{
				var token = _authService.GenerateJwtToken(login.Username, isVip: true);
				return Ok(new { token });
			}
			else
			{
				var token = _authService.GenerateJwtToken(login.Username, isVip: false);
				return Ok(new { token });
			}


		}

	}
}