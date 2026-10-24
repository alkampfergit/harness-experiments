FROM mcr.microsoft.com/devcontainers/dotnet:1-10.0

# Remove the broken YARN apt repository BEFORE features are installed
RUN rm -f /etc/apt/sources.list.d/yarn.list
