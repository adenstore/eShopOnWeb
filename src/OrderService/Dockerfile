#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY . .
#COPY ["src/OrderService/OrderService.csproj", "./OrderService/"]
#RUN dotnet restore "./OrderService/OrderService.csproj"
#COPY . .
WORKDIR "/app/src/OrderService"
RUN dotnet restore

RUN dotnet build "./OrderService.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "./OrderService.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "OrderService.dll"]