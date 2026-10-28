FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /app

# Copy solution and project files first
COPY ArcanoCore.sln .
COPY src/ArcanoCore.Domain/ArcanoCore.Domain.csproj src/ArcanoCore.Domain/
COPY src/ArcanoCore.Application/ArcanoCore.Application.csproj src/ArcanoCore.Application/
COPY src/ArcanoCore.Infrastructure/ArcanoCore.Infrastructure.csproj src/ArcanoCore.Infrastructure/
COPY src/ArcanoCore.API/ArcanoCore.API.csproj src/ArcanoCore.API/

# Restore dependencies
RUN dotnet restore

# Copy all source files and build/publish
COPY . .
WORKDIR /app/src/ArcanoCore.API
RUN dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app
COPY --from=build /app/out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ArcanoCore.API.dll"]
