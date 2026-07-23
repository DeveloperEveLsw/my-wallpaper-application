import { build } from "esbuild";

await build({
  bundle: true,
  entryPoints: ["src/desktop.js"],
  format: "esm",
  legalComments: "none",
  outfile: "desktop/index.js",
  platform: "browser",
  sourcemap: false,
  target: "chrome120",
});
