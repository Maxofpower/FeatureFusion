using FeatureFusion.AppHost;

// Force Docker when DCP autodetection flakes on Windows + Docker Desktop.
var builder = DistributedApplication.CreateBuilder(DockerRuntime.Configure(args));

builder.AddForwardedHeaders();

var infra = builder.AddInfrastructure();

builder.AddProject<Projects.FeatureFusion>("featurefusion")
	.WithEndpoint(7762, targetPort: 5002, scheme: "https", name: "featurefusion-https")
	.WithInfrastructure(infra);

builder.Build().Run();
