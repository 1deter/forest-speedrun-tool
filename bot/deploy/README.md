# Deploying the knowledge bot

Every push to `main` that touches `bot/`, `knowledge/` or the docs it reads
runs `.github/workflows/bot.yml`: tests, an arm64 build, a copy over SSH to
the VPS and a container restart. Until `BOT_DEPLOY_KEY` exists it builds
and tests only. It runs beside forest.deter.cloud on the same VPS, in its
own container, with no open port (a gateway bot only connects out).

## One-time setup (the author)

**1. The Discord application** (a new one, not the QA bot's):
discord.com/developers/applications -> New Application ("Forest Knowledge"
or anything) ->
- *Bot*: Reset Token -> copy it (this is `FOREST_BOT_DISCORD_TOKEN`); turn on
  **Message Content Intent** (needed to read replies and mentions).
- *Installation*: Installation Contexts -> **Guild Install** ticked; Default
  Install Settings -> Guild Install -> scopes `applications.commands` **and
  `bot`** (without `bot` only the command is added - the bot user is not in
  the server and cannot be mentioned); permissions: View Channels, Send
  Messages, Send Messages in Threads, Read Message History. Save, open the
  Install Link, Add to Server. The bot then shows in the member list.
- Right-click the channel(s) it should answer in -> Copy Channel ID (turn on
  Developer Mode in Discord's settings if the option is missing).

**2. The model keys:**
- Gemini: aistudio.google.com -> Get API key -> Create (free tier). This is
  `GEMINI_API_KEY`.
- Mistral (the fallback, optional): console.mistral.ai -> the free
  "Experiment" plan -> API keys -> Create. This is `MISTRAL_API_KEY`.
Both free tiers may use what is sent to improve their products (questions
and the game's code excerpts).

**3. The VPS:**

```sh
git clone --depth 1 https://github.com/1deter/forest-speedrun-tool.git /tmp/fst
sudo sh /tmp/fst/bot/deploy/setup.sh
rm -rf /tmp/fst
sudo nano /opt/forest-bot/.env      # paste the token, keys, channel ids
```

`setup.sh` makes a `forestbotdeploy` user that can only run `deploy.sh`,
puts `compose.yaml` and an `.env` template in `/opt/forest-bot`, downloads
the embedding model (~130 MB) into `/var/lib/forest-bot/embed`, and prints
the deploy key once.

**4. The game's code** (private - never in the repo, never served). Done
2026-10-03 this way: pack the decompiled folder into one archive on the PC
(`tar czf game-code.tar.gz --exclude=Assembly-CSharp/obj Assembly-CSharp`
in `%LOCALAPPDATA%\ForestOverlay\game-src` - 2.5 MB; a session's sandbox
may hide that folder from the author, so copy it to the Desktop), upload it
with Termius SFTP into the home folder (`/home/ubuntu/` - the bot's data
folder is locked to the container's user), then in a VPS terminal **from
the home folder**:

```sh
sudo rm -rf /var/lib/forest-bot/code
sudo mkdir -p /var/lib/forest-bot/code
sudo tar xzf ~/game-code.tar.gz -C /var/lib/forest-bot/code --strip-components=1
sudo chown -R 1654:1654 /var/lib/forest-bot/code
rm ~/game-code.tar.gz
sudo docker restart forest-bot
sudo docker logs --tail 20 forest-bot      # "Code: 3668 files, 4671 types"
```

The Forest is no longer updated, so this is a one-time step. Without it the
bot still answers, without the code tools.

**5. GitHub:** repo -> Settings -> Secrets and variables -> Actions -> New
repository secret: `BOT_DEPLOY_KEY` = the private key setup printed (BEGIN
and END lines included). `DEPLOY_HOST` / `DEPLOY_HOST_KEY` are the site's.

**6. First deploy:** Actions -> bot -> Run workflow. Then
`sudo docker logs --tail 50 forest-bot` should show the knowledge loaded,
the embeddings, the code, the models and `Discord: ready as ...`. The very
first start embeds every knowledge piece on the VPS's CPU (~2 min for 812);
later starts load them from `vectors.db` in milliseconds. The onnxruntime
"Failed to persist telemetry device ID" warning is harmless (read-only
filesystem). `/ask` registered globally can take up to an hour to show;
`FOREST_BOT_TEST_GUILD=<server id>` in `.env` registers it in one server at
once (mentions work immediately either way).

## Day to day

- Logs: `sudo docker logs --tail 100 forest-bot` - one `Ask #n` line per
  question (model, lookups, tokens, time, status).
- Settings: edit `/opt/forest-bot/.env`, then
  `cd /opt/forest-bot && sudo docker compose up -d --force-recreate`.
- The research queue: `sudo docker exec forest-bot dotnet /srv/current/forest-bot.dll queue`
  (or the queue channel, if set).
- Scoring a model change: `sudo docker exec forest-bot dotnet /srv/current/forest-bot.dll eval`
  (spends quota: ~3-6 requests a question; the report lands in
  `/var/lib/forest-bot`).
- Back up: `/var/lib/forest-bot/bot.db` (conversations, feedback, queue).
- Roll back: `ls /opt/forest-bot/app/releases`, then as root
  `ln -sfn releases/<older> /opt/forest-bot/app/current && docker restart forest-bot`.
