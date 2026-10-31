FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy project files first
COPY src/ArcanoCore.Domain/ArcanoCore.Domain.csproj src/ArcanoCore.Domain/
COPY src/ArcanoCore.Application/ArcanoCore.Application.csproj src/ArcanoCore.Application/
COPY src/ArcanoCore.Infrastructure/ArcanoCore.Infrastructure.csproj src/ArcanoCore.Infrastructure/
COPY src/ArcanoCore.API/ArcanoCore.API.csproj src/ArcanoCore.API/

# Restore dependencies
RUN dotnet restore src/ArcanoCore.API/ArcanoCore.API.csproj

# Copy all source files and build/publish
COPY . .
WORKDIR /app/src/ArcanoCore.API
RUN dotnet publish -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .
# DataProtection keys volume; chown so a fresh named volume is writable by the non-root user
RUN mkdir /app/keys && chown $APP_UID /app/keys
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "ArcanoCore.API.dll"]
