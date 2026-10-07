---
name: deploy-watch
description: Watch a push to main deploy the website (forest.deter.cloud) or the knowledge bot to the VPS and confirm it is live - scripts/watch-deploy.py, then a look at the change itself. Use after pushing anything under site/, src/Data/, community/ (the site) or bot/, knowledge/, docs/game-notes.md, docs/savestates.md, docs/run-mode.md, docs/fsm/ (the bot), or when asked whether a deploy went out.
---

# Watch a site / bot deploy

Pushes to `main` deploy by themselves (router rule 14):
`.github/workflows/site.yml` / `bot.yml` test, publish for arm64 and ship
over SSH to `site/deploy/deploy.sh` / `bot/deploy/deploy.sh`, which end
with `docker restart forest-site` / `forest-bot`. One-time setup and the
VPS: `site/deploy/README.md`, `docs/website.md`, `docs/areas/bot.md`.

## Steps

1. **Run the watcher in the background** right after the push:
   ```bash
   python scripts/watch-deploy.py
   ```
   No targets = every one whose newest commit on origin/main is newer
   than its container's restart (`site`, `bot` to name them). Deployed =
   restarted after that commit, the live check answers (the site's
   `/api/spots`, with a new query string each poll - Cloudflare caches a
   `?v=` URL) and the workflow badge is not failing. Exit 1 = timed out
   (900 s, `--timeout`) or failing. It never calls `api.github.com`, and
   neither do you (router rule 4).

2. **Look at the change itself** - "the container restarted" is not
   "the change works":
   - site: load the changed page live in the browser pane with a new
     `?v=` each time; a 3D / map change is compared with a game shot
     before saying "fixed" (memory `look-before-claiming-render-fix`);
   - bot: ask it the question the change was for (`forest-bot search` /
     `ask`, bot/README.md) or check its logs on the VPS
     (`ssh ... "sudo docker logs --tail 50 forest-bot"`, key and host in
     `scripts/session-start.py`).

3. **Not deployed**: the badge says failing -> the workflow run's log on
   github.com (the page, not the API); `ssh` errors -> the VPS
   (`docs/website.md`). Say in chat what is live and what is not.
