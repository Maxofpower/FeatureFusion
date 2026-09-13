FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /build

COPY src/BuildingBlocks/Mediator/*.csproj ./src/BuildingBlocks/Mediator/
COPY src/BuildingBlocks/Mediator.Analyzers/*.csproj ./src/BuildingBlocks/Mediator.Analyzers/
COPY src/BuildingBlocks/Telemetry/*.csproj ./src/BuildingBlocks/Telemetry/
COPY src/BuildingBlocks/Domain/*.csproj ./src/BuildingBlocks/Domain/
COPY src/BuildingBlocks/Domain.EntityFrameworkCore/*.csproj ./src/BuildingBlocks/Domain.EntityFrameworkCore/
COPY src/BuildingBlocks/Idempotency/*.csproj ./src/BuildingBlocks/Idempotency/
COPY src/BuildingBlocks/Mcp/*.csproj ./src/BuildingBlocks/Mcp/
COPY src/BuildingBlocks/Mcp.Analyzers/*.csproj ./src/BuildingBlocks/Mcp.Analyzers/
COPY src/BuildingBlocks/Pagination/*.csproj ./src/BuildingBlocks/Pagination/
COPY src/BuildingBlocks/Pagination.EntityFrameworkCore/*.csproj ./src/BuildingBlocks/Pagination.EntityFrameworkCore/
COPY src/BuildingBlocks/Pagination.Dapper/*.csproj ./src/BuildingBlocks/Pagination.Dapper/
COPY src/Lab/EventBus/*.csproj ./src/Lab/EventBus/
COPY src/Lab/FeatureFusion.ServiceDefaults/*.csproj ./src/Lab/FeatureFusion.ServiceDefaults/
COPY src/Lab/FeatureFusion/*.csproj ./src/Lab/FeatureFusion/

RUN dotnet restore src/Lab/FeatureFusion/FeatureFusion.csproj

COPY src/BuildingBlocks/ ./src/BuildingBlocks/
COPY src/Lab/EventBus/ ./src/Lab/EventBus/
COPY src/Lab/FeatureFusion.ServiceDefaults/ ./src/Lab/FeatureFusion.ServiceDefaults/
COPY src/Lab/FeatureFusion/ ./src/Lab/FeatureFusion/

WORKDIR /build/src/Lab/FeatureFusion
RUN dotnet publish FeatureFusion.csproj -c Release -f net10.0 -o /out --no-restore /p:EnableSourceControlManagerQueries=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /out ./
EXPOSE 5004
ENV ASPNETCORE_URLS=http://+:5004
ENTRYPOINT ["dotnet", "FeatureFusion.dll"]
