FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ErpVendas.Api/ErpVendas.Api.csproj src/ErpVendas.Api/
RUN dotnet restore src/ErpVendas.Api/ErpVendas.Api.csproj
COPY src/ src/
RUN dotnet publish src/ErpVendas.Api/ErpVendas.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "ErpVendas.Api.dll"]
