import { build } from "esbuild";

const common = {
  bundle: true,
  format: "esm",
  platform: "browser",
  target: "chrome120",
  legalComments: "none",
  sourcemap: false,
};

await Promise.all([
  build({
    ...common,
    entryPoints: ["src/desktop.js"],
    outfile: "desktop/index.js",
  }),
  build({
    ...common,
    entryPoints: ["src/popup.js"],
    outfile: "popup/index.js",
  }),
]);
