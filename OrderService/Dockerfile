FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["OrderService/OrderService.csproj", "./OrderService/"]
COPY ["Shared/", "./Shared/"]
RUN dotnet restore "./OrderService/OrderService.csproj"

COPY OrderService/. ./OrderService/
COPY Shared/. ./Shared/
RUN dotnet publish "./OrderService/OrderService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OrderService.dll"]