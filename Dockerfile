# --- Etapa 1: Build ---
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copia todos os arquivos do repositório
COPY . .

# Ajuste o nome abaixo para o seu arquivo real .csproj
# (confirme no GitHub, deve ser algo como ProjetoDePlanejamento.LicensingServer.csproj)
RUN dotnet restore "./ProjetoDePlanejamento.LicensingServer.csproj"
RUN dotnet publish "./ProjetoDePlanejamento.LicensingServer.csproj" -c Release -o /app/publish

# --- Etapa 2: Runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "ProjetoDePlanejamento.LicensingServer.dll"]

