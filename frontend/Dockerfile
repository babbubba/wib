ARG APP=wib-devices

FROM node:20-alpine AS build
ENV NG_CLI_ANALYTICS=false
WORKDIR /app

# Install deps first for better caching
COPY frontend/package.json /app/package.json
RUN --mount=type=cache,target=/root/.npm npm install --no-audit --no-fund --legacy-peer-deps

# Copy the rest of the sources
COPY frontend /app

# Build selected app
ARG APP
RUN --mount=type=cache,target=/root/.npm npm exec ng build ${APP}

FROM nginx:alpine AS final
ARG APP
COPY frontend/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/${APP}/ /usr/share/nginx/html/
EXPOSE 80
CMD ["nginx","-g","daemon off;"]
