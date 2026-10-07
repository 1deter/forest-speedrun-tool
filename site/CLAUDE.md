# Rules for the website (`site/`)

Loaded by itself when work touches `site/`. Commands, the formats, local
work and the map of `docs/website.md`: [`docs/areas/site.md`](../docs/areas/site.md).

- **A push to `main` deploys the live site** (`site/`, `src/Data/`,
  `community/`). Agents never push; the main session does.
- **Security:** CSP - no CDN scripts (vendor them in `wwwroot/vendor`), no
  inline handlers; every write endpoint authenticated; admin endpoints
  under the `admin` group; owner-only actions check ownership. Runner text
  never reaches the DOM as HTML.
- **Check UI changes in a local copy** at desktop and 375 px width; the
  console must be free of errors and CSP violations. There is no node here
  - the browser is the JS check.
- After editing `wwwroot`, **restart the preview** - it versions its
  assets once at startup.
- DB changes follow the existing migration pattern. Viewer-only
  preferences in localStorage, wrapped in try/catch.
- A render fix is done only after a look: render the local page at the
  spot and compare with a game `shot` (gotcha 68; memory
  `look-before-claiming-render-fix`).
- Update `docs/website.md` (or `docs/areas/site.md`) for what you add.
