/* ==========================================================================
   sw.js — the service worker. Makes the whole tutorial work offline.

   WHY THIS EXISTS
   This is a course somebody revises on a train to Bologna, in a waiting room,
   on a phone with two bars. Every page here is already a static file with no
   API call in the reading path — it is *already* offline-capable in every way
   except the one that matters, which is that the browser still asks the
   network for it.

   WHAT IT DOES NOT DO
   It does not touch the accounts API. Sign-in, sync and the leaderboard need
   the network and should fail honestly when it is not there — a cached 200 for
   "here is your profile" would be a lie with someone's progress attached. Only
   GET requests for the site's own static files are cached; anything under
   /api/ is passed straight through.

   ONE THING TO KNOW ABOUT SERVICE WORKERS
   They only run over http(s). Opening index.html from disk (file://) skips all
   of this silently, which is correct — nothing to cache, nothing to serve — and
   is why site.js checks the protocol before registering.

   Covered in: site/README.md
   ========================================================================== */

/* The chapter list, imported rather than duplicated. chapters.js assigns onto
   `self`, which is why that works here — see the note at the bottom of it. */
importScripts("assets/chapters.js");

/* BUMP THIS ON EVERY CONTENT CHANGE.
   The cache name is the version. A new name means install() builds a fresh
   cache and activate() deletes every older one, which is the whole upgrade
   mechanism — there is no partial invalidation and you do not want one. Forget
   to bump it and returning visitors keep last month's chapters, with no error
   anywhere, which is the single most common service-worker bug. */
const CACHE = "logiflow-academy-v12";

const SHELL = [
  "./",
  "index.html",
  "404.html",
  "dashboard.html",
  "review.html",
  "viva.html",
  "exam.html",
  "simulate.html",
  "notes.html",
  "glossary.html",
  "italiano.html",
  "cv.html",
  "account.html",
  "signin.html",
  "register.html",
  "reset.html",
  "favicon.svg",
  "manifest.webmanifest",
  "assets/style.css",
  "assets/learn.css",
  "assets/chapters.js",
  "assets/quizzes-1.js",
  "assets/quizzes-2.js",
  "assets/quizzes-3.js",
  "assets/rules.js",
  "assets/glossary.js",
  "assets/jargon.js",
  "assets/jargon-marks.js",
  "assets/italiano.js",
  "assets/italiano-panel.js",
  "assets/store.js",
  "assets/site.js",
  "assets/quiz.js",
  "assets/learn.js",
  "assets/viva.js",
  "assets/simulate.js",
  "assets/cv.js",
  "assets/notes.js",
  "assets/account.js",
  "assets/auth-page.js",
];

/* Every chapter, from the manifest. 56 files nobody has to list by hand. */
const CHAPTER_FILES = (self.CHAPTERS || []).map((c) => "chapters/" + c.id + ".html");

/* Fetch one URL and store it under the URL WE ASKED FOR, as a fresh response.
   cache.add() would be shorter and is subtly wrong on any host that redirects.

   Cloudflare's html_handling: "auto-trailing-slash" answers /chapters/x.html
   with a 307 to /chapters/x. cache.add() follows it and stores the result with
   `redirected: true` and `url` pointing at the redirected address. The browser
   then REFUSES to use that response for a navigation — "a redirected response
   was used for a request whose redirect mode is not follow" — so the first
   visit registers this worker and every chapter link breaks from then on.

   It is invisible to curl, which has no service worker, and invisible locally,
   where python's http.server redirects nothing. It only appears on the
   deployed site, on the second visit.

   Rebuilding the Response drops the redirect flag and pins the entry to the
   requested URL, so the site is correct on a host that redirects, one that does
   not, and from disk. Deliberately not "detect a redirect and handle it": the
   normalisation is unconditional so there is no path where it is skipped. */
async function precache(cache, url) {
  try {
    const response = await fetch(url, { redirect: "follow" });

    if (!response.ok) {
      throw new Error("HTTP " + response.status);
    }

    await cache.put(
      url,
      new Response(await response.blob(), {
        status: 200,
        statusText: "OK",
        headers: response.headers,
      })
    );
    return true;
  } catch (err) {
    /* Reported, not swallowed. The caller decides what a failure means: fatal
       for the shell, survivable for one chapter — see install() below. */
    console.warn("[sw] could not precache", url, err);
    return false;
  }
}

self.addEventListener("install", (event) => {
  event.waitUntil(
    (async () => {
      const cache = await caches.open(CACHE);

      /* THE SHELL IS REQUIRED, AND A FAILURE HERE MUST FAIL THE INSTALL.
         activate() below deletes every other cache wholesale, so a worker that
         activates over a half-filled cache has just thrown away the complete
         copy and put a worse one in its place. Rejecting instead leaves the OLD
         worker in charge of the OLD, complete cache, and the upgrade is simply
         retried on the next visit. Nothing is lost by refusing to upgrade; a
         great deal is lost by upgrading badly.

         The previous version logged every failure and carried on, so install
         always reported success — one flaky response out of ninety simultaneous
         requests was enough to activate a worker over an emptied cache, and
         from then on every page change fell through to the network. */
      const failed = [];
      await Promise.all(
        SHELL.map(async (url) => {
          if (!(await precache(cache, url))) failed.push(url);
        })
      );
      if (failed.length) {
        throw new Error("[sw] shell incomplete, install aborted: " + failed.join(", "));
      }

      /* Chapters are best-effort, by contrast. One chapter missing from the
         offline copy is a gap; the fetch handler falls through to the network
         for it and the visitor never notices unless they are on a train. That
         is not worth refusing an upgrade over. */
      await Promise.all(CHAPTER_FILES.map((url) => precache(cache, url)));

      /* Take over as soon as the install finishes rather than waiting for every
         tab to close — and only now, with the shell verified present. Safe here
         because the cache is versioned wholesale: there is no state in an old
         page that a new worker could corrupt. */
      await self.skipWaiting();
    })()
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((names) => Promise.all(names.filter((n) => n !== CACHE).map((n) => caches.delete(n))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (event) => {
  const request = event.request;

  /* Only our own GETs. A POST to the accounts API must never be served from a
     cache, and neither must anything from another origin. */
  if (request.method !== "GET" || new URL(request.url).origin !== self.location.origin) {
    return;
  }

  if (new URL(request.url).pathname.includes("/api/")) {
    return;
  }

  event.respondWith(
    caches.match(request).then((cached) => {
      /* CACHE FIRST, with a background refresh — "stale while revalidate".
         The reasoning is specific to this site: the content is a course, not a
         feed. A chapter that is one version old is completely fine to read and
         a spinner is not, so the cached copy wins the race every time and the
         network updates it for next visit.

         Network-first would be right for anything where staleness is a
         correctness problem. It is not right for a tutorial. */
      const network = fetch(request)
        .then((response) => {
          if (response && response.status === 200 && response.type === "basic") {
            const copy = response.clone();
            caches.open(CACHE).then((cache) => cache.put(request, copy));
          }
          return response;
        })
        .catch(() => cached);

      return cached || network;
    })
  );
});
