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
const CACHE = "logiflow-academy-v1";

const SHELL = [
  "./",
  "index.html",
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

/* Every chapter, from the manifest. 47 files nobody has to list by hand. */
const CHAPTER_FILES = (self.CHAPTERS || []).map((c) => "chapters/" + c.id + ".html");

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE).then((cache) =>
      /* addAll is atomic: one 404 rejects the whole install and the old worker
         stays in charge. That is the behaviour you want — a half-populated
         cache serving some pages and failing others is worse than not
         upgrading. Each file is added individually here anyway, because a
         single typo in the list above should not make the site un-upgradeable;
         the failures are logged and the install still completes. */
      Promise.all(
        SHELL.concat(CHAPTER_FILES).map((url) =>
          cache.add(url).catch((err) => {
            console.warn("[sw] could not precache", url, err);
          })
        )
      )
    )
  );

  /* Take over as soon as the install finishes rather than waiting for every
     tab to close. Safe here because the cache is versioned wholesale: there is
     no state in an old page that a new worker could corrupt. */
  self.skipWaiting();
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
