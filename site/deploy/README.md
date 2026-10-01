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
- Admin token: `sudo cat /opt/forest-site/.env` (also holds
  `FOREST_ORIGIN_SECRET` once the origin lock is on) - send it as the
  `X-Admin-Token` header to `/api/admin/...` (flagged runs, hide / delete a
  run, spot submissions, reset a runner's token, ban).
- Back up: `/var/lib/forest-site` (the database and every uploaded run).
- Roll back: `ls /opt/forest-site/app/releases`, then as root
  `ln -sfn releases/<older> /opt/forest-site/app/current && docker restart forest-site`.

## Hardening (security review, 2026-10-01)

**Done on the live VPS (the author, 2026-10-01).** Kept as the record and
for a rebuild. Since then the repo's `compose.yaml` uses the `forest-site`
network and `setup.sh` creates it; a fresh server still needs step 2's
secret and Caddy joining that network.

**1. The locked-down container** (`compose.yaml`: the image's non-root
`app` user, read-only filesystem, no capabilities, memory / process caps).
`deploy.sh` only restarts the container, so the new `compose.yaml` needs
setup once more (it keeps the data, token and key; no Caddyfile argument =
Caddy untouched):

```sh
git clone --depth 1 https://github.com/1deter/forest-speedrun-tool.git /tmp/fst
sudo sh /tmp/fst/site/deploy/setup.sh
rm -rf /tmp/fst
sudo docker logs --tail 20 forest-site      # "Data in /data; ..." and no errors
curl -s -o /dev/null -w '%{http_code}\n' https://forest.deter.cloud/api/spots   # 200
```

It also gives `/var/lib/forest-site` to uid 1654. If the site does not
come back: `sudo docker logs forest-site`; to undo, put the previous
`compose.yaml` back (`git show 274466e:site/deploy/compose.yaml`) into
`/opt/forest-site/` and `cd /opt/forest-site && sudo docker compose up -d
--force-recreate` (root can still read the data).

**2. The origin lock.** Cloudflare's `CF-Connecting-IP` names the visitor
for the rate limits, but anyone who finds the VPS's address (its
certificate is public, scanners index it) can talk to Caddy directly and
send any `CF-Connecting-IP` - unlimited registrations / uploads. The
site refuses every request without a secret header once
`FOREST_ORIGIN_SECRET` is set; Cloudflare adds the header:

1. Make a secret: `head -c 24 /dev/urandom | od -An -tx1 | tr -d ' \n'`
2. Cloudflare -> deter.cloud -> Rules -> Transform Rules -> *Modify
   Request Header* -> Create: custom filter *Hostname equals
   forest.deter.cloud*, then *Set static*, header `X-Forest-Origin`,
   value = the secret. Deploy. (The site ignores the header until step 3.)
3. On the VPS: `sudo sh -c 'echo FOREST_ORIGIN_SECRET=<secret> >>
   /opt/forest-site/.env'`, then `cd /opt/forest-site && sudo docker
   compose up -d --force-recreate` (`docker restart` does not re-read
   `.env`).
4. Check: the curl above answers 200; straight to the VPS
   `curl -sk -o /dev/null -w '%{http_code}\n' --resolve
   forest.deter.cloud:443:<VPS IP> https://forest.deter.cloud/api/spots`
   answers **403**. The game's uploads go through Cloudflare, so they
   keep working.

Undo: delete the line from `.env` and recreate as in 3.

**3. A network of its own (optional).** The site shares
`caddy-navidrome` with Navidrome, so a compromised site container could
reach Navidrome directly. Better: a network only the site and Caddy are
on - `sudo docker network create forest-site`, add it to Caddy's own
compose file (`networks:` of the caddy service + `forest-site: external:
true`), `docker compose up -d` there, then in `/opt/forest-site/compose.yaml`
replace `caddy-navidrome` with `forest-site` (both places) and recreate.
Caddy still reaches `forest-site:8080` by name.
