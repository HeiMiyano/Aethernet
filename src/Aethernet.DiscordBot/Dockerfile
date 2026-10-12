# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Aethernet.DiscordBot/Aethernet.DiscordBot.csproj
RUN dotnet publish src/Aethernet.DiscordBot/Aethernet.DiscordBot.csproj \
    -c Release -o /app /p:UseAppHost=false

# Plain runtime image, not aspnet — this is a console app with no HTTP listener.
FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Aethernet.DiscordBot.dll"]
