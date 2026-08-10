using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FeatureFusion.Features.MediatorDemo.Commands;
using FeatureFusion.Features.MediatorDemo.Queries;
using FluentAssertions;
using IntegrationTests.Aspire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IntegrationTests.Api;

/// <summary>
/// HTTP integration coverage for MediatorDemo vertical slice
/// (FluentValidation → ValidationBehavior → handler, optional UseTelemetry wrap).
/// </summary>
[Collection(AspireCollection.Name)]
public sealed class MediatorDemoApiTests
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly HttpClient _client;

	public MediatorDemoApiTests(AspireFixture fixture)
	{
		_client = fixture.CreateClient(new WebApplicationFactoryClientOptions
		{
			AllowAutoRedirect = false
		});
	}

	[Fact]
	public async Task Echo_Valid_Returns_Ok()
	{
		var response = await _client.PostAsJsonAsync(
			"/api/v2/mediator-demo/echo",
			new { message = "hello-mediator" });

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var payload = await response.Content.ReadFromJsonAsync<EchoResponse>(JsonOptions);
		payload.Should().NotBeNull();
		payload!.Echo.Should().Be("hello-mediator");
		payload.TimestampUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
	}

	[Fact]
	public async Task Echo_Empty_Message_Returns_ValidationProblem()
	{
		var response = await _client.PostAsJsonAsync(
			"/api/v2/mediator-demo/echo",
			new { message = "" });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(JsonOptions);
		problem.Should().NotBeNull();
		problem!.Title.Should().Be("One or more validation errors occurred.");
		problem.Status.Should().Be(400);
		var messageErrors = GetPropertyErrors(problem, nameof(EchoCommand.Message));
		messageErrors.Should().Contain(m => m.Contains("required", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task Echo_Message_Too_Long_Returns_ValidationProblem()
	{
		var response = await _client.PostAsJsonAsync(
			"/api/v2/mediator-demo/echo",
			new { message = new string('x', 201) });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(JsonOptions);
		problem.Should().NotBeNull();
		var messageErrors = GetPropertyErrors(problem!, nameof(EchoCommand.Message));
		messageErrors.Should().Contain(m => m.Contains("200", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Echo_FaultTrigger_Returns_ServerError()
	{
		var response = await _client.PostAsJsonAsync(
			"/api/v2/mediator-demo/echo",
			new { message = EchoCommand.FaultTrigger });

		// Unhandled handler exception after validation — default exception middleware (not ValidationExceptionHandler).
		response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
	}

	[Fact]
	public async Task Echo_Malformed_Json_DoesNotSucceed()
	{
		using var content = new StringContent("{ not-json", Encoding.UTF8, "application/json");
		var response = await _client.PostAsync("/api/v2/mediator-demo/echo", content);

		// JSON input formatter faults are not FluentValidation — status is host-dependent (400 or 500).
		response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.InternalServerError);
	}

	[Fact]
	public async Task Status_Returns_Ok_With_ActivitySource_Hint()
	{
		var response = await _client.GetAsync("/api/v2/mediator-demo/status");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var payload = await response.Content.ReadFromJsonAsync<EchoStatusResponse>(JsonOptions);
		payload.Should().NotBeNull();
		payload!.Status.Should().Be("ready");
		payload.ActivitySource.Should().Be("BuildingBlocks.Mediator");
		payload.Hint.Should().NotBeNullOrWhiteSpace();
	}

	private static string[] GetPropertyErrors(ValidationProblemDetails problem, string propertyName)
	{
		var match = problem.Errors.FirstOrDefault(kv =>
			kv.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
		match.Value.Should().NotBeNull($"expected validation errors for '{propertyName}'");
		return match.Value;
	}
}
