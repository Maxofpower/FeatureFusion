FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build

COPY src/FeatureFusion/*.csproj ./src/FeatureFusion/
COPY src/EventBusRabbitMQ/*.csproj ./src/EventBusRabbitMQ/
COPY src/FeatureFusion.AppHost.ServiceDefaults/*.csproj ./src/FeatureFusion.AppHost.ServiceDefaults/

RUN dotnet restore src/FeatureFusion/FeatureFusion.csproj

COPY src/FeatureFusion/ ./src/FeatureFusion/
COPY src/EventBusRabbitMQ/ ./src/EventBusRabbitMQ/
COPY src/FeatureFusion.AppHost.ServiceDefaults/ ./src/FeatureFusion.AppHost.ServiceDefaults/

WORKDIR /build/src/FeatureFusion
RUN dotnet publish FeatureFusion.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out ./
EXPOSE 5004
ENV ASPNETCORE_URLS=http://+:5004
HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
  CMD curl -f http://localhost:5004/health || exit 1
ENTRYPOINT ["dotnet", "FeatureFusion.dll"]
