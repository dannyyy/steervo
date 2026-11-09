FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra@sha256:b4855d6d1c557c19a7c7165b354a138b47c17b29669ad0b3b22fb046a1b84fd8
WORKDIR /app
COPY --chown=1654:1654 .artifacts/publish/ ./
COPY --chown=1654:1654 deploy/container/keys/ /var/steervo/keys/
USER 1654:1654
ENV ASPNETCORE_HTTP_PORTS=8080 DataProtection__Path=/var/steervo/keys
EXPOSE 8080
ENTRYPOINT ["dotnet", "Steervo.Web.dll"]
