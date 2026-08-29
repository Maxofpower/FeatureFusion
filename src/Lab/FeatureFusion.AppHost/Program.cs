using FeatureFusion.AppHost;

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
	.WithUrl("/swagger/index.html?urls.primaryName=v2", "Swagger v2")
	.WithInfrastructure(infra)
	.WithSigNozOtlpExporter(signoz);

builder.Build().Run();
