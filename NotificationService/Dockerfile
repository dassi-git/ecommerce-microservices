FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["NotificationService/NotificationService.csproj", "./NotificationService/"]
COPY ["Shared/", "./Shared/"]
RUN dotnet restore "./NotificationService/NotificationService.csproj"

COPY NotificationService/. ./NotificationService/
COPY Shared/. ./Shared/
RUN dotnet publish "./NotificationService/NotificationService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "NotificationService.dll"]