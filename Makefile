TAG = ffmpeg7.1.1
VERSION := $(shell git symbolic-ref -q --short HEAD)_$(shell git rev-parse --short HEAD)

all: arm64

arm64:
	docker buildx build --build-arg INFO_VERSION="$(VERSION)" -t ersatztv:latest -f docker/arm64/Dockerfile .

push:
	docker image tag ersatztv:latest ersatztv:$(TAG)
	docker image tag ersatztv:$(TAG) registry.rt/ersatztv:$(TAG)
	docker image push registry.rt/ersatztv:$(TAG)
