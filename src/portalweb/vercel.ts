import type { VercelConfig } from '@vercel/config/v1';

/**
 * Project configuration for `morganhacks-portalweb`.
 *
 * Root Directory is deliberately NOT here — Vercel has to know it before it can
 * find this file, so it stays a project setting (`src/portalweb`).
 */
export const config: VercelConfig = {
  framework: 'nextjs',

  /**
   * Skip the build when nothing under this directory changed. Run from the
   * project's Root Directory, so it means "did src/portalweb change?" — which
   * keeps a portaladmin-only commit from rebuilding the public site.
   * Exit 0 skips, exit 1 builds.
   */
  ignoreCommand: 'git diff --quiet HEAD^ HEAD ./',
};
