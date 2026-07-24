# syntax=docker/dockerfile:1
#
# Builds the marketing site and exports the static output — nothing runs from this image. Caddy
# serves the files from a host directory (./landing-dist), so the only thing we need out of Docker
# is a Node toolchain the VM does not have to install:
#
#     docker build --target export --output type=local,dest=./landing-dist ./landing
#
# ops/build-landing.sh does exactly that, into a temp directory first. Use it rather than this
# command directly — a bare --output leaves files from the previous build behind.

FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM scratch AS export
COPY --from=build /app/dist /
