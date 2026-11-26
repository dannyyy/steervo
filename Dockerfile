FROM mcr.microsoft.com/dotnet/aspnet:11.0-resolute-chiseled-extra@sha256:4f2b9809c1974acd91dc04777bf464808fb5d22a3455c6657a97847077878936
WORKDIR /app
COPY --chown=1654:1654 .artifacts/publish/ ./
COPY --chown=1654:1654 deploy/container/keys/ /var/steervo/keys/
COPY --chown=1654:1654 deploy/container/documents/ /var/steervo/documents/
USER 1654:1654
ENV ASPNETCORE_HTTP_PORTS=8080 DataProtection__Path=/var/steervo/keys
EXPOSE 8080
ENTRYPOINT ["dotnet", "Steervo.Web.dll"]
