# Bundled webfonts

The wizard serves its own fonts rather than naming system families like `Segoe UI` or `Consolas`.
Chromium shapes text with HarfBuzz on every platform, so a font shipped with the app measures
identically on Windows, Linux and macOS. A system font does not: fontconfig on the CI Linux image
resolves the stack to different faces with different advance widths, prose re-wraps, and the height
of a full-page screenshot moves — which is what `ScreenSnapshotTests` compares. Pinning the faces is
what lets those PNG baselines hold on any OS.

Each file is a subset of an upstream face, built with `fonttools` (see `contributing.md`). Coverage
is declared by the `unicode-range` descriptors on the `@font-face` rules in `css/app.css`, and
`RepoContractTests.ShippedFontsCoverRenderedText` fails if the wizard renders a character those
ranges do not cover — a character outside them would fall through to a system font and reintroduce
the drift these files exist to remove.

## Shipping the fonts is not sufficient on its own

Bundling the faces pins *which* outlines are used. Three rules in `css/app.css` pin *how they are
measured*, and each was needed to get the Linux render to match the Windows baselines. They look
like cosmetic details and are not:

- `text-rendering: geometricPrecision` on `html, body`. By default the rasterizer rounds advance
  widths onto its hinting grid, and FreeType and DirectWrite round differently, so the same
  sentence wraps at a different word per platform. This measures from the font's own metrics.
- `font-family` on `code, pre, kbd, samp`. The UA stylesheet sets a font on these elements, and
  that beats inheritance — without the rule a `<code>` renders in the platform's monospace font
  even inside a styled `<pre>`.
- `line-height: 1` on `code, kbd, samp`. A line's height is the union of the strut and every
  inline box on it, each placed from its own font's ascent and descent — values rounded to whole
  pixels differently per platform. Keeping the inline box inside the strut makes prose lines
  containing inline code depend on the strut alone.

Form controls are pinned with `font-family: inherit` for the same reason: they do not inherit it.

| File | Upstream | Version | Licence |
| ---- | -------- | ------- | ------- |
| `open-sans.woff2` | [Open Sans](https://github.com/googlefonts/opensans) | 3.003 | SIL Open Font License 1.1 |
| `work-sans-arrows.woff2` | [Work Sans](https://github.com/weiweihuanghuang/Work-Sans) | 2.009 | SIL Open Font License 1.1 |
| `ubuntu-mono.woff2` | Ubuntu Mono, Canonical Ltd. | 0.862 | Ubuntu Font Licence 1.0 |

`open-sans.woff2` keeps the weight axis variable (300-800) with the width axis pinned to 100.
`ubuntu-mono.woff2` keeps its weight axis variable (400-700). Open Sans has no arrow glyphs, so
U+2190-2193 is served from `work-sans-arrows.woff2` under the same `SponsorCheck Sans` family name.

## Copyright notices

Reproduced verbatim from each font's own `name` table, which remains intact in the shipped subsets
(name records are preserved when subsetting, so the authoritative statement still travels inside
every file):

- Open Sans — `Copyright 2020 The Open Sans Project Authors (https://github.com/googlefonts/opensans)`
- Work Sans — `Copyright 2019 The Work Sans Project Authors (https://github.com/weiweihuanghuang/Work-Sans)`
- Ubuntu Mono — `Copyright 2011, 2022 Canonical Ltd. Licensed under the Ubuntu Font Licence 1.0`

Open Sans and Work Sans carry this licence record: *"This Font Software is licensed under the SIL
Open Font License, Version 1.1. This license is available with a FAQ at:
https://scripts.sil.org/OFL"*. Neither declares a Reserved Font Name, so the subsets keep the
upstream family names in their `name` tables. The Ubuntu Font Licence 1.0 is at
<https://ubuntu.com/legal/font-licence>.
