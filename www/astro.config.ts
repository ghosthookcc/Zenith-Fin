import qwikdev from "@qwikdev/astro";
import { defineConfig } from "astro/config";

import fs from "fs";

import node from '@astrojs/node';
import tailwindcss from "@tailwindcss/vite";

// https://astro.build/config
export default defineConfig({
  output: "static",
  adapter: node({ mode: "standalone" }),
  integrations: [qwikdev()],
  vite : {
    server: {
        port: 4444,
        host: true,
        https: {
            key: fs.readFileSync("./localhost+2-key.pem"),
            cert: fs.readFileSync("./localhost+2.pem"),
        },
    },
    plugins: [tailwindcss()]
  }
});
