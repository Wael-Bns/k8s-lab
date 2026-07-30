# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY K8sLabApi.csproj .
RUN dotnet restore K8sLabApi.csproj
COPY . .
RUN dotnet publish K8sLabApi.csproj -c Release -o /app/publish --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Run as non-root, like you'd want in a real cluster.
RUN useradd -m appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "K8sLabApi.dll"]
