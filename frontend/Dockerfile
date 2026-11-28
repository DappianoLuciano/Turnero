# syntax=docker/dockerfile:1
# Panel + web de reservas (PWA) servidos por Nginx, que además reenvía /api y /health a la API.
# Build desde la RAÍZ del repo:  docker build -f frontend/Dockerfile -t turnero-web .
# Variable de entorno en runtime: API_UPSTREAM (por ejemplo http://api:8080).

FROM node:22-alpine AS build
WORKDIR /src
RUN corepack enable
COPY product.json ./
COPY frontend/package.json frontend/pnpm-lock.yaml frontend/
RUN cd frontend && pnpm install --frozen-lockfile
COPY frontend/ frontend/
RUN cd frontend && pnpm build

FROM nginx:1.29-alpine AS runtime
COPY frontend/nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /src/frontend/dist /usr/share/nginx/html
ENV API_UPSTREAM=http://api:8080
EXPOSE 80
