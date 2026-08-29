# Traefik

This stack assumes a Traefik instance already runs on the host and owns ports 80
and 443. It joins that instance's network rather than starting its own, so the
homelab keeps a single ingress and a single certificate store.

## What the stack expects

- An external Docker network, named by `TRAEFIK_NETWORK` in `.env`.
- An entrypoint called `websecure` on 443.
- A certificate resolver named by `CERT_RESOLVER` in `.env`.

## Routing

Both containers answer on one hostname, and the priority decides which:

| Router       | Rule                                        | Priority |
|--------------|---------------------------------------------|----------|
| `split-api`  | `Host(PUBLIC_HOST)` and `/api` or `/hubs`   | 20       |
| `split-web`  | `Host(PUBLIC_HOST)`                         | 10       |

One hostname is deliberate. The browser then makes no cross-origin requests, the
refresh cookie stays first-party, and no CORS configuration is needed in
production. `Cors:AllowedOrigins` exists for local development, where Vite serves
the app from a different port.

## WebSockets

Traefik proxies WebSockets without extra configuration, so `/hubs/sync` works
through the same router. The API service is labelled with a sticky cookie: with a
single replica it changes nothing, but it means adding a replica later does not
silently break SignalR by moving a connection mid-negotiation.

## A minimal reference Traefik

If there is no Traefik yet, this is the shape the labels expect. It is not part
of the stack: run it separately so its lifecycle is independent.

```yaml
name: traefik

services:
  traefik:
    image: traefik:v3.6
    restart: unless-stopped
    command:
      - --providers.docker=true
      - --providers.docker.exposedByDefault=false
      - --entrypoints.web.address=:80
      - --entrypoints.web.http.redirections.entryPoint.to=websecure
      - --entrypoints.web.http.redirections.entryPoint.scheme=https
      - --entrypoints.websecure.address=:443
      - --certificatesresolvers.letsencrypt.acme.email=you@example.com
      - --certificatesresolvers.letsencrypt.acme.storage=/acme/acme.json
      - --certificatesresolvers.letsencrypt.acme.tlschallenge=true
    ports:
      - 80:80
      - 443:443
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - acme:/acme
    networks:
      - traefik

volumes:
  acme:

networks:
  traefik:
    name: traefik
```
