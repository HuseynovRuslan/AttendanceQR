# Frontend image — multi-stage: build the Vite SPA with Node, serve static files with nginx.
# Build CONTEXT is the frontend/ directory:
#   docker build -f frontend/Dockerfile -t attendanceqr-web --build-arg VITE_API_URL=https://api.example.com frontend

# ---- build stage ----
FROM node:20-alpine AS build
WORKDIR /app

# Install deps first (cached until package-lock.json changes).
COPY package.json package-lock.json ./
RUN npm ci

# VITE_API_URL is baked into the bundle at build time — Vite inlines VITE_* at build,
# so it must be supplied here (NOT at container runtime).
ARG VITE_API_URL
ENV VITE_API_URL=$VITE_API_URL

COPY . .
RUN npm run build

# ---- runtime stage: nginx serving the static bundle ----
FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
