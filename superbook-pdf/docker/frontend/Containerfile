# superbook-pdf Frontend - Nginx Static Server with Reverse Proxy
FROM docker.io/nginx:1.25-alpine

LABEL maintainer="superbook-pdf"
LABEL description="superbook-pdf Frontend Web UI"
LABEL component="frontend"

# Install envsubst for environment variable substitution
RUN apk add --no-cache gettext

# Remove default nginx config
RUN rm /etc/nginx/conf.d/default.conf

# Copy nginx configuration
COPY nginx.conf /etc/nginx/nginx.conf
COPY default.conf.template /etc/nginx/templates/default.conf.template

# Copy static files
COPY static/ /usr/share/nginx/html/

# Environment variables for backend connection
ENV BACKEND_HOST=backend
ENV BACKEND_PORT=8080

# Expose HTTP port
EXPOSE 80

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
    CMD wget -q --spider http://localhost/ || exit 1

# Use nginx's envsubst on startup
CMD ["/bin/sh", "-c", "envsubst '${BACKEND_HOST} ${BACKEND_PORT}' < /etc/nginx/templates/default.conf.template > /etc/nginx/conf.d/default.conf && nginx -g 'daemon off;'"]
