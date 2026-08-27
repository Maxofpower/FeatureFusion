FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build

COPY src/BuildingBlocks/Mediator/*.csproj ./src/BuildingBlocks/Mediator/
COPY src/BuildingBlocks/Mediator.Analyzers/*.csproj ./src/BuildingBlocks/Mediator.Analyzers/
COPY src/BuildingBlocks/Telemetry/*.csproj ./src/BuildingBlocks/Telemetry/
COPY src/Lab/EventBus/*.csproj ./src/Lab/EventBus/
COPY src/Lab/FeatureFusion.ServiceDefaults/*.csproj ./src/Lab/FeatureFusion.ServiceDefaults/
COPY src/Lab/FeatureFusion/*.csproj ./src/Lab/FeatureFusion/

RUN dotnet restore src/Lab/FeatureFusion/FeatureFusion.csproj

COPY src/BuildingBlocks/ ./src/BuildingBlocks/
COPY src/Lab/EventBus/ ./src/Lab/EventBus/
COPY src/Lab/FeatureFusion.ServiceDefaults/ ./src/Lab/FeatureFusion.ServiceDefaults/
COPY src/Lab/FeatureFusion/ ./src/Lab/FeatureFusion/

WORKDIR /build/src/Lab/FeatureFusion
RUN dotnet publish FeatureFusion.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out ./
EXPOSE 5004
ENV ASPNETCORE_URLS=http://+:5004
HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
  CMD curl -f http://localhost:5004/health || exit 1
ENTRYPOINT ["dotnet", "FeatureFusion.dll"]
