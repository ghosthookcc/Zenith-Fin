import qwikdev from "@qwikdev/astro";
import { defineConfig } from "astro/config";

import fs from "fs";

// https://astro.build/config
export default defineConfig({
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
  },
});
