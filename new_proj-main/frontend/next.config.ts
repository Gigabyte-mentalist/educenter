import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // The container image runs `.next/standalone/server.js` — a traced copy of just the files the app needs, no node_modules.
  output: "standalone",

  // Nothing gained by telling a scanner which framework this is.
  poweredByHeader: false,

  // The dev server refuses cross-origin requests for its own assets unless the origin is listed here, so a tester
  // opening the app by this machine's LAN or Tailscale address would get a half-loaded page. Development only —
  // `next build` output is unaffected.
  allowedDevOrigins: ["192.168.10.108", "100.91.165.14", "192.168.10.*"],
};

export default nextConfig;
