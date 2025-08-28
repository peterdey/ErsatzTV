FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-arm64v8 AS dotnet-runtime

FROM lscr.io/linuxserver/jellyfin:10.10.7 AS runtime-base

RUN cd /tmp && \
    curl -fsSLO https://github.com/tsukumijima/libmali-rockchip/releases/download/v1.9-1-2131373/libmali-valhall-g610-g13p0-gbm_1.9-1_arm64.deb && \
    dpkg -i libmali-valhall-g610-g13p0-gbm_1.9-1_arm64.deb && \
    rm -f libmali-valhall-g610-g13p0-gbm_1.9-1_arm64.deb && \
    apt-get update && \
    apt-get install --yes clinfo && \
    apt-get -y autoremove && apt-get clean && rm -rf /var/lib/apt/lists/*

RUN apt-get update && apt-get --yes install fontconfig nano && apt-get -y autoremove && apt-get clean && rm -rf /var/lib/apt/lists/*

RUN groupadd -g 110 render && \
    groupadd -g 4000 ersatztv && \
    useradd -u 4003 -g 4000 -G 44,110 -s /usr/sbin/nologin ersatztv && \
    mkdir /transcode && \
    chown -R ersatztv:ersatztv /config /transcode

COPY docker/arm64/ffmpeg-script /usr/lib/jellyfin-ffmpeg
RUN mv /usr/lib/jellyfin-ffmpeg/ffmpeg /usr/lib/jellyfin-ffmpeg/ffmpeg2 && \
    mv /usr/lib/jellyfin-ffmpeg/ffmpeg-script /usr/lib/jellyfin-ffmpeg/ffmpeg && \
    chmod a+x /usr/lib/jellyfin-ffmpeg/ffmpeg

ENV PATH="$PATH:/usr/lib/jellyfin-ffmpeg"

COPY --from=dotnet-runtime /usr/share/dotnet /usr/share/dotnet

# https://hub.docker.com/_/microsoft-dotnet
FROM mcr.microsoft.com/dotnet/sdk:9.0-noble-arm64v8 AS build
RUN apt-get update && apt-get install -y ca-certificates gnupg
WORKDIR /source

# copy csproj and restore as distinct layers
COPY *.sln .
COPY artwork/* ./artwork/
COPY ErsatzTV/*.csproj ./ErsatzTV/
COPY ErsatzTV.Application/*.csproj ./ErsatzTV.Application/
COPY ErsatzTV.Core/*.csproj ./ErsatzTV.Core/
COPY ErsatzTV.FFmpeg/*.csproj ./ErsatzTV.FFmpeg/
COPY ErsatzTV.Infrastructure/*.csproj ./ErsatzTV.Infrastructure/
COPY ErsatzTV.Infrastructure.Sqlite/*.csproj ./ErsatzTV.Infrastructure.Sqlite/
COPY ErsatzTV.Infrastructure.MySql/*.csproj ./ErsatzTV.Infrastructure.MySql/
COPY ErsatzTV.Scanner/*.csproj ./ErsatzTV.Scanner/
RUN dotnet restore -r linux-arm64 ErsatzTV/

# copy everything else and build app
COPY ErsatzTV/. ./ErsatzTV/
COPY ErsatzTV.Application/. ./ErsatzTV.Application/
COPY ErsatzTV.Core/. ./ErsatzTV.Core/
COPY ErsatzTV.FFmpeg/. ./ErsatzTV.FFmpeg/
COPY ErsatzTV.Infrastructure/. ./ErsatzTV.Infrastructure/
COPY ErsatzTV.Infrastructure.Sqlite/. ./ErsatzTV.Infrastructure.Sqlite/
COPY ErsatzTV.Infrastructure.MySql/. ./ErsatzTV.Infrastructure.MySql/
COPY ErsatzTV.Scanner/. ./ErsatzTV.Scanner/
WORKDIR /source/ErsatzTV.Scanner
ARG INFO_VERSION="unknown"
RUN dotnet publish ErsatzTV.Scanner.csproj --framework net9.0 -c release -o /app --runtime linux-arm64 --no-self-contained --no-restore -p:DebugType=Embedded -p:PublishSingleFile=false -p:PublishTrimmed=false -p:InformationalVersion=${INFO_VERSION}
WORKDIR /source/ErsatzTV
RUN sed -i '/Scanner/d' ErsatzTV.csproj
RUN dotnet publish ErsatzTV.csproj --framework net9.0 -c release -o /app --runtime linux-arm64 --no-self-contained --no-restore -p:DebugType=Embedded -p:PublishSingleFile=false -p:PublishTrimmed=false -p:InformationalVersion=${INFO_VERSION}

# final stage/image
FROM runtime-base
ENV FONTCONFIG_PATH=/etc/fonts
RUN fc-cache update
WORKDIR /app
COPY --from=build /app ./
ENV ETV_CONFIG_FOLDER=/config
ENV ETV_TRANSCODE_FOLDER=/transcode
ENTRYPOINT ["./ErsatzTV"]
