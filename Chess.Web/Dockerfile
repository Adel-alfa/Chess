# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Chess.Web/Chess.Web.csproj", "Chess.Web/"]
COPY ["Chess.Application/Chess.Application.csproj", "Chess.Application/"]
COPY ["Chess.Domain/Chess.Domain.csproj", "Chess.Domain/"]
COPY ["Chess.Infrastructure/Chess.Infrastructure.csproj", "Chess.Infrastructure/"]
RUN dotnet restore "./Chess.Web/Chess.Web.csproj"
COPY . .
RUN dotnet restore "./Chess.Web/Chess.Web.csproj" --force
WORKDIR "/src/Chess.Web"

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Chess.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Chess.Web.dll"]