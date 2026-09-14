using FeatureFusion.AppHost;

if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"))
	&& string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")))
{
	Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
	Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
}

// Force Docker when DCP autodetection flakes on Windows + Docker Desktop.
var builder = DistributedApplication.CreateBuilder(DockerRuntime.Configure(args));

builder.AddForwardedHeaders();

var infra = builder.AddInfrastructure();
var signoz = builder.AddSigNoz("signoz")
	.WithUiFromConfiguration(builder.Configuration)
	.WithDashboards();

builder.AddProject<Projects.FeatureFusion>("featurefusion")
	.WithHttpEndpoint(port: 5141, name: "http")
	.WithEndpoint(7762, targetPort: 5002, scheme: "https", name: "featurefusion-https")
	.WithUrl("/swagger/index.html?urls.primaryName=v1", "Swagger v1")
	.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
	.WithInfrastructure(infra)
	.WithSigNozOtlpExporter(signoz);

builder.Build().Run();
