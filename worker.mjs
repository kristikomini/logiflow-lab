/* ===========================================================================
   worker.mjs — the smallest possible Worker, and a note on why it exists.

   Cloudflare's asset server has one setting, `html_handling`, that decides two
   unrelated things at once:

     · whether "/" is served from "/index.html";  and
     · whether "/x.html" REDIRECTS to "/x".

   This site needs the first and must not have the second. Every link in it is
   an explicit .html path — the site is designed to open from a USB stick, where
   there is no server to rewrite anything — and the service worker precaches
   those exact paths. Under a redirecting mode the precache stores redirected
   responses, and a redirected response served for a navigation is an error the
   browser refuses outright, so the site stops working offline. That is the
   failure the worker exists to prevent, so it cannot be the price of fixing the
   home page.

   `html_handling: "none"` therefore stays, and this file supplies the missing
   half: a directory path is rewritten INTERNALLY to its index.html. A rewrite,
   not a redirect — the browser never learns it happened, and the URL does not
   change.

   AND IT SERVES THE BRANDED 404, which is the other half of the same argument.
   `not_found_handling` is "none", NOT "404-page": under "404-page" the asset
   layer answers a navigation miss by itself and this Worker is never invoked,
   so "/" would never reach the rewrite above and the home page would break —
   while curl and fetch(), which are not navigations, kept looking healthy. So
   the miss is handled here instead.

   The status stays 404. A styled page returned as 200 is a soft 404: search
   engines index it, and it hides broken links from anyone testing with curl.
   Without site/404.html this Worker returned the asset server's own answer,
   which is a 404 with a ZERO-BYTE body — and an empty-bodied 404 is what makes
   a browser paint its own "cannot find the page" error screen instead.

   IT RUNS ALMOST NEVER. With `main` and `assets` both configured, Cloudflare
   serves a request that matches a file directly and only invokes this Worker
   when nothing matched. Every chapter, script and stylesheet bypasses it
   entirely; "/" and genuine misses are the only URLs that reach it.
   =========================================================================== */

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    /* A directory: "/" or "/anything/". Serve its index.html, if it has one. */
    if (url.pathname.endsWith("/")) {
      const index = new URL(url.pathname + "index.html", url.origin);
      const hit = await env.ASSETS.fetch(new Request(index, request));
      if (hit.status !== 404) return hit;
    }

    const response = await env.ASSETS.fetch(request);

    /* A genuine miss. Serve the site's own 404 page, but keep the 404 status.
       Built as a fresh Request rather than handing the binding a bare URL, for
       the same reason as the branch above: that is the form the assets binding
       is documented to take, and it keeps the two call sites identical. */
    if (response.status === 404) {
      const page = await env.ASSETS.fetch(
        new Request(new URL("/404.html", url.origin), { method: "GET" })
      );
      if (page.status === 200) {
        return new Response(page.body, {
          status: 404,
          headers: { "content-type": "text/html; charset=utf-8" },
        });
      }
    }

    return response;
  },
};
