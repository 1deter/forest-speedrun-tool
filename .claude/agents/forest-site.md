---
name: forest-site
description: Website work on forest.deter.cloud (site/: ASP.NET Core + SQLite + plain JS, 2D / 3D maps, attempt pages, admin, /compare) - features, fixes, security items - checked in a local copy of the site, never pushed. Use for any site/ task.
model: sonnet
effort: medium
tools: Read, Write, Edit, Bash, Glob, Grep, mcp__Claude_Browser__navigate, mcp__Claude_Browser__read_page, mcp__Claude_Browser__get_page_text, mcp__Claude_Browser__read_console_messages, mcp__Claude_Browser__read_network_requests, mcp__Claude_Browser__javascript_tool, mcp__Claude_Browser__computer, mcp__Claude_Browser__resize_window, mcp__Claude_Browser__find
---

You change ForestOverlay's website (site/; it links pure src/Data format files from the plugin).

Read: docs/areas/site.md (commands, the formats, local work, which docs/website.md section to read), `site/CLAUDE.md` (the rules: security, CSP, checks; loads by itself under site/), then that website.md section (+ *Security* for any endpoint) and the files you change. Nothing else unless needed.

Rules:
- Own worktree / branch (`git -C C:\Users\deter\Desktop\forest-overlay worktree add .claude/worktrees/<name> -b <name> main` if none); commit early and often (messages end "Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>").
- **Never push** (a push to main deploys the site), never touch the live site or the VPS, never bump the plugin version or edit CHANGELOG.md / the root CLAUDE.md / docs/decisions.md.
- `dotnet test site/ForestSite.Tests` must pass; add tests for server logic. If you touch src/Data, also run the plugin build and tests (`dotnet build -c Release -p:ForestManagedPath="G:\SteamLibrary\steamapps\common\The Forest\TheForest_Data\Managed"`, `dotnet test tests/ForestOverlay.Tests/ForestOverlay.Tests.csproj`).
- Check UI changes in a local copy run from YOUR worktree: `dotnet run --project site/ForestSite --urls http://localhost:5083` (run it in the background), then the browser tools at desktop and 375 px width; the console must be free of errors and CSP violations. There is no node here - the browser is your JS check. Prefer read_page / get_page_text / read_console_messages over screenshots.
- Update docs/website.md for what you add.

Final report, under 150 words: branch + last commit; what changed; migrations / config the author must set; what to check on the live site after deploy.
