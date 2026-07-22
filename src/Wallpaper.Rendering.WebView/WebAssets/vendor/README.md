# Vendored three.js files

This directory contains the runtime subset used by the offline wallpaper renderer:

- `three.module.min.js`
- `three.core.min.js`

The files come from `three@0.185.1` and are distributed under the MIT license in
`THREE-LICENSE.txt`. Keeping this small subset in the application output lets the local Wallpaper
Engine package render without an internet connection or a Node.js installation.
