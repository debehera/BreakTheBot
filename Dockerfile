# ---- Stage 1: build the app ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project files first so package downloads are cached between builds.
COPY BreakTheBot.sln ./
COPY src/BreakTheBot.Web/BreakTheBot.Web.csproj src/BreakTheBot.Web/
COPY tests/BreakTheBot.Tests/BreakTheBot.Tests.csproj tests/BreakTheBot.Tests/
RUN dotnet restore

# Now copy everything else and publish only the web app.
COPY . .
RUN dotnet publish src/BreakTheBot.Web/BreakTheBot.Web.csproj -c Release -o /app/publish --no-restore

# ---- Stage 2: the small image that actually runs ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# The host tells the app which port to use through the PORT variable (8080 if it is not set).
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet BreakTheBot.Web.dll"]