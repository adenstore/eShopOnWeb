#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

# The build context is the repository root, not this folder: central package management keeps
# TargetFramework and every package version in Directory.Packages.props at the root, and this
# project references OrderService.Contracts outside src/.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY . .
WORKDIR "/app/src/BasketService"
RUN dotnet restore

RUN dotnet build "./BasketService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "./BasketService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BasketService.dll"]
