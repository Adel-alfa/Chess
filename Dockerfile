# Build from the repository root so all project references are available.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

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
RUN dotnet publish "./Chess.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Chess.Web.dll"]