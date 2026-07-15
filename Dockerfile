# =========================
# Build stage
# =========================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY ["viora-BE/viora-BE.csproj", "viora-BE/"]
COPY ["Viora.Application/Viora.Application.csproj", "Viora.Application/"]
COPY ["Viora.Domain/Viora.Domain.csproj", "Viora.Domain/"]
COPY ["Viora.Infrastructure/Viora.Infrastructure.csproj", "Viora.Infrastructure/"]

RUN dotnet restore "viora-BE/viora-BE.csproj"

COPY . .

RUN dotnet publish "viora-BE/viora-BE.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore

# =========================
# Runtime stage
# =========================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
    
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 10000

ENTRYPOINT ["dotnet", "viora-BE.dll"]
