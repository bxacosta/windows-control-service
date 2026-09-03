# banner

The picture at the top of the repository's README.

```powershell
node banner/generate.mjs
```

It renders the interface the service actually serves — `src/WindowsControlService/wwwroot`, read
straight from disk — against a machine that has something to show, screenshots the Applications
section and composes the picture around it. **Nothing is installed, no policy is applied and a
running service is not touched.** The API is answered from inside the page, the same way
`scripts/interface-dom.mjs` does it, and both go through `scripts/lib/browser.mjs` so there is one
copy of the serve-and-drive-a-browser code rather than two.

What the picture shows is the interface in a state it reaches on its own: the last rule is asking
whether it should be removed because the generator presses the button, and the dot on the Devices
tab is there because the event stream is answered with the snapshot the service sends on connect.
Nothing is dressed up in markup to look as if someone had.

Regenerate it whenever the interface changes. The clock is frozen in `machine.mjs`, so two runs of
the same interface produce the same picture: a diff on `banner.png` means the interface moved, not
that the file was rebuilt.

| File | What it is |
|---|---|
| `generate.mjs` | Captures the section and composes the image |
| `machine.mjs` | The simulated machine: health, blocked applications, USB state, access history |
| `banner.png` | The output, 1200×630 at 2x |
| `banner.html` | The intermediate page that gets screenshotted. Untracked; useful when a change to the composition does not look like it should |

## What is in it, and what is not

The blocked applications are an example of what someone would plausibly block, not a claim about
any of them, and the addresses are from the ranges reserved for documentation (RFC 5737) so no
screenshot of this project ever publishes a real one. The machine name and the account are
invented. If you change `machine.mjs`, keep it that way.

1200×630 is also the shape GitHub uses for a repository's social preview, so the one image serves
both. It is set under Settings → General → Social preview; nothing does that automatically.

## Requirements

Node, and Microsoft Edge at its default location. Point somewhere else with
`--browser="C:\path\to\msedge.exe"`; `--port` moves the debugging port if 9403 is taken.
