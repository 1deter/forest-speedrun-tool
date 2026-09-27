# Deploying forest.deter.cloud

Every push to `main` that touches `site/` (or the shared `src/Data` files,
or `community/`) runs `.github/workflows/site.yml`: tests, an arm64 build,
then a copy over SSH to the VPS and a container restart. Until the three
secrets below exist it builds and tests only.

## One-time setup

**1. Cloudflare:** DNS -> Add record: type `A`, name `forest`, the VPS's IP,
proxied (orange cloud). SSL/TLS stays on Full.

**2. The VPS** (Ubuntu, Docker, Caddy in `~/website`):

```sh
git clone --depth 1 https://github.com/1deter/forest-speedrun-tool.git /tmp/fst
sudo sh /tmp/fst/site/deploy/setup.sh ~/website/Caddyfile
rm -rf /tmp/fst
```

It makes a `forestdeploy` user that can only run `deploy.sh`, puts the
container's `compose.yaml` in `/opt/forest-site` (data in
`/var/lib/forest-site`), adds `forest.deter.cloud` to the Caddyfile (a
backup is kept as `Caddyfile.before-forest`) and reloads Caddy, then prints
the three secrets. The private key is printed once and not kept on the
server.

**3. GitHub:** the repo -> Settings -> Secrets and variables -> Actions ->
New repository secret, three times:

| Name | Value |
|---|---|
| `DEPLOY_HOST` | the VPS's public IP |
| `DEPLOY_HOST_KEY` | the `ssh-ed25519 ...` line setup printed |
| `DEPLOY_KEY` | the private key setup printed, BEGIN and END lines included |

**4. First deploy:** Actions -> site -> Run workflow (or any push to
`site/`). The last step checks `https://forest.deter.cloud/api/spots`.

## Day to day

- Logs: `sudo docker logs --tail 100 forest-site`
- Admin token: `sudo cat /opt/forest-site/.env` - send it as the
  `X-Admin-Token` header to `/api/admin/...` (flagged runs, hide / delete a
  run, spot submissions, reset a runner's token, ban).
- Back up: `/var/lib/forest-site` (the database and every uploaded run).
- Roll back: `ls /opt/forest-site/app/releases`, then as root
  `ln -sfn releases/<older> /opt/forest-site/app/current && docker restart forest-site`.
