# banner

The picture at the top of the repository's README, also used as GitHub's social preview (Settings → General → Social
preview; nothing sets it automatically).

```powershell
node banner/generate.mjs
```

It renders `src/WindowsControlService/wwwroot` from disk against a simulated machine, screenshots the Applications
section and composes the picture around it. Nothing is installed, no policy is applied and no running service is
touched: the API is answered from inside the page.

The state shown is one the interface reaches on its own, not markup dressed up to look like it.

Regenerate whenever the interface changes. The clock is frozen in `machine.mjs`, so a diff on
`banner.png` means the interface moved.

| File          | What it is                                                                                                 |
|---------------|------------------------------------------------------------------------------------------------------------|
| `machine.mjs` | Health, blocked applications, USB state, access history                                                    |
| `banner.png`  | The output, 1200×630 at 2x, the size GitHub expects for a social preview                                   |
| `banner.html` | Intermediate page that gets screenshotted. Untracked; useful when a composition change does not look right |

The machine name and the account are invented, and the blocked applications are an example of what someone would
plausibly block, not a claim about any of them. Addresses come from the ranges reserved for documentation (RFC 5737);
keep it that way when editing `machine.mjs`.

## Requirements

Node, and Microsoft Edge at its default location. Use `--browser="C:\path\to\msedge.exe"` to point elsewhere, and
`--port` if 9403 is taken.
