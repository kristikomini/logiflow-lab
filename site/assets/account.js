/* ==========================================================================
   account.js — sign in, and keep one profile across devices.

   The site works completely without this. Everything is stored locally first
   and an account is purely a way to (a) let several people share one machine
   or one deployment, and (b) carry your progress from a laptop to a phone.
   If the API is unreachable, nothing here degrades the site — it goes quiet.

   Talks to LogiFlow.Academy.Api (src/LogiFlow.Academy.Api). The contract:

     POST  /api/auth/register        { username, displayName, password }
                                       -> { ..., recoveryCode }   ONCE, never again
     POST  /api/auth/login           { username, password }
     POST  /api/auth/refresh         { refreshToken }
     POST  /api/auth/logout          { refreshToken }
     POST  /api/auth/forgot-password { username, recoveryCode }
                                       -> { resetToken, expiresInSeconds }
     POST  /api/auth/reset-password  { resetToken, newPassword }
                                       -> { ..., recoveryCode }   a fresh one
     POST  /api/auth/recovery-code                                (authenticated)
                                       -> { recoveryCode }        replaces the old one
     GET   /api/profile                       -> { data, updatedAt, revision }
     PUT   /api/profile        { data, updatedAt, baseRevision }
     GET   /api/leaderboard                   -> [ { displayName, xp, ... } ]

   THE RECOVERY CODE is the only thing here that cannot be re-fetched. The server stores a
   hash of it and nothing else, so the two responses that carry one are the only moments it
   exists in readable form. It is deliberately never written to localStorage: somewhere a
   person will actually keep it is the point, and a browser somebody can clear is not that.

   THE MERGE is the interesting part. Two devices editing the same profile is
   the normal case, not the exception — you answer questions on a laptop and
   review cards on a phone. Last-write-wins would silently throw away one of
   them, so `merge()` below combines them field by field, taking the better
   of each: the higher XP, the better chapter score, the longer streak, the
   union of notes and badges, and the more advanced card schedule.
   ========================================================================== */
(function () {
  "use strict";

  if (!window.LF) return;

  var K_BASE  = "logiflow.api.base";
  var K_TOK   = "logiflow.api.token";
  var K_REF   = "logiflow.api.refresh";
  var K_USER  = "logiflow.api.user";
  var K_REV   = "logiflow.api.revision";

  function get(k)      { try { return localStorage.getItem(k) || ""; } catch (e) { return ""; } }
  function set(k, v)   { try { if (v) localStorage.setItem(k, v); else localStorage.removeItem(k); } catch (e) { /* ignore */ } }

  // Chapter pages sit one folder down; every other page is at the site root.
  var atRoot = !document.body.getAttribute("data-chapter");
  function rel(f) { return (atRoot ? "" : "../") + f; }

  /**
   * Where the API lives.
   *
   * When the page itself is served over http(s) — that is, somebody is running
   * the Academy API and it is serving this site as static files — the API is
   * the same origin and no configuration is needed. Opened from disk
   * (file://), there is no origin to guess, so it stays empty until somebody
   * types one into the account panel.
   */
  function base() {
    var stored = get(K_BASE);
    if (stored) return stored.replace(/\/+$/, "");
    if (location.protocol === "http:" || location.protocol === "https:") return location.origin;
    return "";
  }

  /**
   * Point the site at a different Academy API.
   *
   * Changing the address necessarily signs you out: the access token in this browser was
   * minted by the old server and means nothing to the new one. Local progress is left
   * exactly where it is, which is what makes this a safe thing to let anyone do.
   */
  function setBase(value) {
    set(K_BASE, String(value || "").trim().replace(/\/+$/, ""));
    signOutLocal();
    paint();
    return base();
  }

  var state = {
    user: null,
    status: "offline",       // offline | signed-out | signed-in | error | syncing
    message: "",
  };

  try { state.user = get(K_USER) ? JSON.parse(get(K_USER)) : null; } catch (e) { state.user = null; }
  if (state.user) state.status = "signed-in";
  else if (base()) state.status = "signed-out";

  /* ======================================================================
     HTTP
     ====================================================================== */

  function api(path, options, retryOnce) {
    var b = base();
    if (!b) return Promise.reject(new Error("No API configured."));

    var opts = options || {};
    var headers = { "Content-Type": "application/json" };
    var token = get(K_TOK);
    if (token) headers.Authorization = "Bearer " + token;

    return fetch(b + path, {
      method: opts.method || "GET",
      headers: headers,
      body: opts.body ? JSON.stringify(opts.body) : undefined,
    }).then(function (res) {
      if (res.status === 401 && retryOnce !== false && get(K_REF)) {
        return refresh().then(function () { return api(path, options, false); });
      }
      // Drain the (empty) body rather than walking away from it: an unread response
      // shows up in the network panel as an aborted request, which looks like a bug.
      if (res.status === 204) return res.text().then(function () { return null; });
      return res.json().catch(function () { return null; }).then(function (payload) {
        if (!res.ok) {
          var msg = (payload && (payload.detail || payload.title || payload.error)) ||
                    ("Request failed (" + res.status + ")");
          var err = new Error(msg);
          err.status = res.status;
          err.payload = payload;
          throw err;
        }
        return payload;
      });
    });
  }

  function refresh() {
    var r = get(K_REF);
    if (!r) return Promise.reject(new Error("Not signed in."));
    return fetch(base() + "/api/auth/refresh", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ refreshToken: r }),
    }).then(function (res) {
      if (!res.ok) { signOutLocal(); throw new Error("Session expired. Sign in again."); }
      return res.json();
    }).then(storeSession);
  }

  function storeSession(payload) {
    if (!payload) return payload;
    set(K_TOK, payload.accessToken || "");
    set(K_REF, payload.refreshToken || "");
    if (payload.user) {
      state.user = payload.user;
      set(K_USER, JSON.stringify(payload.user));
    }
    state.status = "signed-in";
    paint();
    return payload;
  }

  function signOutLocal() {
    set(K_TOK, ""); set(K_REF, ""); set(K_USER, ""); set(K_REV, "");
    state.user = null;
    state.status = base() ? "signed-out" : "offline";
  }

  /* ======================================================================
     Who is listening

     The top-bar button is no longer the only thing that draws this state:
     signin.html, register.html and account.html all render from it too. They
     subscribe rather than poll, because "syncing" and "synced at 14:02" are
     states nobody would think to poll for.
     ====================================================================== */

  var listeners = [];

  function onChange(fn) {
    if (typeof fn === "function") listeners.push(fn);
    return fn;
  }

  function emit() {
    for (var i = 0; i < listeners.length; i++) {
      // One page's rendering bug must not stop the next listener — or, worse,
      // abort the sync that triggered this.
      try { listeners[i](state); } catch (e) { /* ignore */ }
    }
  }

  /* ======================================================================
     The merge
     ====================================================================== */

  function newer(a, b) { return Date.parse(a || 0) >= Date.parse(b || 0) ? a : b; }

  /**
   * Combine two spaced-repetition maps. The device that reviewed a card most
   * recently owns its schedule, because that is the one whose interval reflects
   * the latest answer; reps and lapses take the higher of the two, since both
   * devices really did happen.
   */
  function mergeSchedules(local, remote) {
    var out = Object.assign({}, remote || {});
    Object.keys(local || {}).forEach(function (id) {
      var a = local[id], b = out[id];
      if (!b) { out[id] = a; return; }
      var lead = (a.last || "") >= (b.last || "") ? a : b;
      out[id] = {
        ease: lead.ease, interval: lead.interval, reps: Math.max(a.reps, b.reps),
        lapses: Math.max(a.lapses, b.lapses),
        due: lead.due, last: (a.last || "") >= (b.last || "") ? a.last : b.last,
        grade: lead.grade,
      };
    });
    return out;
  }

  /**
   * Combine two versions of the same profile, taking the better of each
   * field. Deliberately conservative: nothing is ever removed by a merge,
   * because losing a month of study to a sync bug is unrecoverable and having
   * a stale note is not.
   */
  function merge(local, remote) {
    if (!remote) return local;
    if (!local) return remote;

    var out = JSON.parse(JSON.stringify(remote));

    out.name = (Date.parse(local.updatedAt || 0) > Date.parse(remote.updatedAt || 0))
      ? (local.name || remote.name) : (remote.name || local.name);
    out.xp = Math.max(local.xp || 0, remote.xp || 0);
    out.updatedAt = newer(local.updatedAt, remote.updatedAt);
    out.createdAt = (Date.parse(local.createdAt || 0) && Date.parse(local.createdAt) < Date.parse(remote.createdAt || 0))
      ? local.createdAt : (remote.createdAt || local.createdAt);
    out.examBest = Math.max(local.examBest || 0, remote.examBest || 0);
    out.examCount = Math.max(local.examCount || 0, remote.examCount || 0);

    // Chapters: keep the better score and the earlier read date.
    out.chapters = Object.assign({}, remote.chapters);
    Object.keys(local.chapters || {}).forEach(function (id) {
      var a = local.chapters[id], b = out.chapters[id];
      if (!b) { out.chapters[id] = a; return; }
      out.chapters[id] = {
        read: a.read || b.read,
        readAt: a.readAt && b.readAt ? (a.readAt < b.readAt ? a.readAt : b.readAt) : (a.readAt || b.readAt),
        best: Math.max(a.best || 0, b.best || 0),
        attempts: Math.max(a.attempts || 0, b.attempts || 0),
        lastAt: newer(a.lastAt, b.lastAt),
        seconds: Math.max(a.seconds || 0, b.seconds || 0),
        perfect: a.perfect || b.perfect,
      };
    });

    // Cards: the schedule that has progressed further wins, but lapses add up
    // — a card you have failed on two devices really is a card you keep failing.
    out.cards = mergeSchedules(local.cards, remote.cards);

    // The viva deck is scheduled by the same algorithm, so it merges by the
    // same rule. It is a separate map: see the comment on `viva` in store.js.
    out.viva = mergeSchedules(local.viva, remote.viva);

    // Notes: union by id, newest edit wins.
    var byId = {};
    (remote.notes || []).forEach(function (n) { byId[n.id] = n; });
    (local.notes || []).forEach(function (n) {
      var e = byId[n.id];
      if (!e || Date.parse(n.updatedAt || 0) > Date.parse(e.updatedAt || 0)) byId[n.id] = n;
    });
    out.notes = Object.keys(byId).map(function (k) { return byId[k]; });

    // Badges: once earned, earned. Keep the earlier timestamp.
    out.badges = Object.assign({}, remote.badges);
    Object.keys(local.badges || {}).forEach(function (id) {
      if (!out.badges[id] || local.badges[id] < out.badges[id]) out.badges[id] = local.badges[id];
    });

    // History: sum per day is wrong (the same session could be counted twice),
    // so take the larger figure for each day.
    out.history = Object.assign({}, remote.history);
    Object.keys(local.history || {}).forEach(function (d) {
      var a = local.history[d], b = out.history[d];
      if (!b) { out.history[d] = a; return; }
      out.history[d] = {
        xp: Math.max(a.xp || 0, b.xp || 0),
        cards: Math.max(a.cards || 0, b.cards || 0),
        answers: Math.max(a.answers || 0, b.answers || 0),
        correct: Math.max(a.correct || 0, b.correct || 0),
        minutes: Math.max(a.minutes || 0, b.minutes || 0),
        cardXp: Math.max(a.cardXp || 0, b.cardXp || 0),
        viva: Math.max(a.viva || 0, b.viva || 0),
      };
    });

    // Streak: the longer current run wins, and best is the maximum of both.
    var ls = local.streak || {}, rs = remote.streak || {};
    out.streak = {
      count: (ls.lastDay || "") >= (rs.lastDay || "") ? (ls.count || 0) : (rs.count || 0),
      lastDay: (ls.lastDay || "") >= (rs.lastDay || "") ? ls.lastDay : rs.lastDay,
      best: Math.max(ls.best || 0, rs.best || 0),
    };

    // Attempts: concatenate, dedupe on (chapter, timestamp), keep the last 400.
    var seen = {}, attempts = [];
    (remote.attempts || []).concat(local.attempts || []).forEach(function (a) {
      var k = a.ch + "|" + a.at;
      if (seen[k]) return;
      seen[k] = 1;
      attempts.push(a);
    });
    attempts.sort(function (a, b) { return (a.at || "") < (b.at || "") ? -1 : 1; });
    out.attempts = attempts.slice(-400);

    return out;
  }

  /* ======================================================================
     Sync
     ====================================================================== */

  var syncTimer = null;
  var syncing = false;

  function pull() {
    return api("/api/profile").then(function (payload) {
      if (!payload || !payload.data) return null;
      return payload;
    });
  }

  function push(profile, baseRevision) {
    return api("/api/profile", {
      method: "PUT",
      body: {
        data: profile,
        updatedAt: profile.updatedAt,
        baseRevision: baseRevision ? parseInt(baseRevision, 10) : 0,
      },
    });
  }

  /** Pull, merge, push. Safe to call often; it coalesces. */
  function sync(force) {
    if (state.status !== "signed-in" || syncing) return Promise.resolve(false);
    syncing = true;
    setStatus("syncing", "");

    return pull()
      .then(function (remote) {
        var local = window.LF.profile;
        var merged = remote ? merge(local, remote.data) : local;
        var changed = force || !remote ||
          JSON.stringify(merged) !== JSON.stringify(remote.data);

        if (remote && JSON.stringify(merged) !== JSON.stringify(local)) {
          window.LF.replace(merged);
        }
        if (!changed) {
          set(K_REV, String(remote.revision || 0));
          return false;
        }
        return push(window.LF.profile, remote ? remote.revision : 0)
          .then(function (saved) {
            if (saved && saved.revision) set(K_REV, String(saved.revision));
            window.LF.profile.remote.dirty = false;
            return true;
          });
      })
      .then(function (result) {
        syncing = false;

        // Stamped here rather than in the push branch above, because a sync that found
        // nothing to send still talked to the server and still confirms the two copies
        // agree — and "last synced" that only moves when something changed is a clock
        // that stops whenever you are up to date.
        //
        // Left in memory deliberately: persisting it would go through LF.save(), which
        // emits "change", which schedules another sync four seconds later, which stamps
        // it again. A page that wants the value simply reads it after a sync completes.
        window.LF.profile.remote.syncedAt = new Date().toISOString();

        setStatus("signed-in", "Synced " + new Date().toLocaleTimeString());
        return result;
      })
      .catch(function (err) {
        syncing = false;
        setStatus("error", err.message || "Sync failed");
        return false;
      });
  }

  function scheduleSync() {
    if (state.status !== "signed-in") return;
    if (syncTimer) clearTimeout(syncTimer);
    syncTimer = setTimeout(function () { syncTimer = null; sync(false); }, 4000);
  }

  window.LF.on("change", scheduleSync);
  window.addEventListener("beforeunload", function () {
    // Best effort. A real flush needs sendBeacon, which cannot set an
    // Authorization header — so the periodic sync above is what actually
    // protects the data, and this is only a nicety.
    if (syncTimer) { clearTimeout(syncTimer); }
  });

  /* ======================================================================
     Auth actions
     ====================================================================== */

  /**
   * Create an account, sign in, and hand the caller the one-time recovery code.
   *
   * The code is returned rather than stored, and the sync is awaited before resolving, so the
   * page that called this can show the code and know the upload already happened. Whoever
   * calls this MUST put the code in front of the user — nothing else ever can.
   */
  function register(username, displayName, password) {
    var code = "";
    return api("/api/auth/register", {
      method: "POST",
      body: { username: username, displayName: displayName, password: password },
    })
      .then(function (payload) {
        code = (payload && payload.recoveryCode) || "";
        return storeSession(payload);
      })
      .then(function () { return sync(true); })
      .then(function () { return code; });
  }

  function login(username, password) {
    return api("/api/auth/login", { method: "POST", body: { username: username, password: password } })
      .then(storeSession)
      .then(function () { return sync(true); });
  }

  /**
   * Step one of a reset: swap a username and recovery code for a short-lived ticket.
   *
   * Anonymous, and deliberately so — the whole point is that the caller cannot sign in.
   */
  function forgotPassword(username, recoveryCode) {
    return api("/api/auth/forgot-password", {
      method: "POST",
      body: { username: username, recoveryCode: recoveryCode },
    });
  }

  /** Step two: spend the ticket, land signed in, and receive a replacement recovery code. */
  function resetPassword(resetToken, newPassword) {
    var code = "";
    return api("/api/auth/reset-password", {
      method: "POST",
      body: { resetToken: resetToken, newPassword: newPassword },
    })
      .then(function (payload) {
        code = (payload && payload.recoveryCode) || "";
        return storeSession(payload);
      })
      .then(function () { return sync(true); })
      .then(function () { return code; });
  }

  function logout() {
    var r = get(K_REF);
    var done = function () { signOutLocal(); paint(); };
    if (!r) { done(); return Promise.resolve(); }
    return api("/api/auth/logout", { method: "POST", body: { refreshToken: r } })
      .then(done, done);
  }

  /* ======================================================================
     UI
     ====================================================================== */

  var button, panel;

  function setStatus(status, message) {
    state.status = status;
    state.message = message || "";
    paint();
  }

  function paint() {
    paintButton();
    emit();
  }

  function paintButton() {
    if (!button) return;
    var dot = state.status === "signed-in" ? "on" : state.status === "error" ? "err" : "";
    var label = state.user
      ? (state.user.displayName || state.user.username || "Account").split(" ")[0]
      : (state.status === "offline" ? "Local" : "Sign in");
    /* The label sits in its own span because the narrow topbar hides it and
       keeps only the dot — see the 760px block in learn.css. Hiding text is
       also hiding the button's accessible name, so an explicit aria-label
       carries it instead of relying on the text node being there. */
    button.innerHTML = '<span class="sync-dot ' + dot + '"></span>' +
      '<span class="btn-label">' + esc(label) + "</span>";
    button.title = state.user
      ? "Signed in as " + (state.user.username || "") + (state.message ? " — " + state.message : "")
      : state.status === "offline"
        ? "Progress is saved in this browser only. Click to connect an account server."
        : "Sign in to keep your progress across devices";
    button.setAttribute("aria-label", button.title);
  }

  function esc(s) {
    return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;")
      .replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  function openPanel() {
    if (panel) { closePanel(); return; }
    panel = document.createElement("div");
    panel.className = "account-panel";
    panel.innerHTML = state.user ? signedInHtml() : signedOutHtml();
    document.body.appendChild(panel);
    wirePanel();

    setTimeout(function () {
      document.addEventListener("mousedown", outside);
      document.addEventListener("keydown", onEsc);
    }, 0);
  }

  function closePanel() {
    if (!panel) return;
    panel.remove();
    panel = null;
    document.removeEventListener("mousedown", outside);
    document.removeEventListener("keydown", onEsc);
  }

  function outside(e) {
    if (!panel) return;
    if (panel.contains(e.target) || (button && button.contains(e.target))) return;
    closePanel();
  }
  function onEsc(e) { if (e.key === "Escape") closePanel(); }

  function signedOutHtml() {
    var b = base();
    return "<h3>Your progress</h3>" +
      '<p class="subtle" style="font-size:13.5px;margin:0">' +
        (b
          ? "Everything is already saved in this browser. Sign in to keep it across devices, or to share this machine with someone else."
          : "Everything is saved in this browser only. To sync, point this at a running LogiFlow Academy API.") +
      "</p>" +
      (b
        ? '<form id="acctForm">' +
            '<label for="acctUser">Username</label>' +
            '<input id="acctUser" type="text" autocomplete="username" required ' +
              'spellcheck="false" autocapitalize="none">' +
            '<label for="acctPass">Password</label>' +
            '<input id="acctPass" type="password" autocomplete="current-password" required minlength="10">' +
            '<div class="account-row">' +
              '<button type="submit" class="btn primary">Sign in</button>' +
              '<button type="button" class="btn" data-act="server">Server…</button>' +
            "</div>" +
            '<p class="account-msg" id="acctMsg"></p>' +
          "</form>" +
          // Creating an account is NOT offered in here. Registration hands back a recovery
          // code shown exactly once, and a 330px popover that closes when you click the page
          // behind it is the wrong place to put a secret somebody has to write down.
          '<p class="account-foot">No account yet? ' +
          '<a href="' + rel("register.html") + '">Create one on the full page</a> — it gives you ' +
          "a recovery code to keep, which is the only way back in if you forget your password.</p>"
        : '<div class="account-row"><button type="button" class="btn primary" data-act="server">Set the server address</button></div>') +
      '<p class="account-foot">Local progress is never uploaded until you sign in, and signing out leaves it here untouched. ' +
      '<a href="#" data-act="export">Export a backup</a> &middot; <a href="#" data-act="import">Import</a></p>' +
      '<p class="account-foot"><a href="' + rel("signin.html") + '">Full sign-in page</a> &middot; ' +
      '<a href="' + rel("reset.html") + '">Forgotten your password?</a></p>';
  }

  function signedInHtml() {
    var m = window.LF.mastery();
    return "<h3>" + esc(state.user.displayName || state.user.username) + "</h3>" +
      '<p class="subtle" style="font-size:13px;margin:0">@' + esc(state.user.username || "") + "</p>" +
      '<p style="font-size:14px;margin:.8em 0 0"><span class="sync-dot ' +
        (state.status === "error" ? "err" : "on") + '"></span>' +
        esc(state.message || "Syncing automatically") + "</p>" +
      '<p style="font-size:14px;margin:.5em 0 0">' + m.percent + "% mastered &middot; " +
        window.LF.profile.xp + " XP &middot; " + window.LF.level().name + "</p>" +
      '<div class="account-row">' +
        '<button type="button" class="btn primary" data-act="syncnow">Sync now</button>' +
        '<button type="button" class="btn" data-act="logout">Sign out</button>' +
      "</div>" +
      '<p class="account-msg" id="acctMsg"></p>' +
      '<p class="account-foot">Signing out leaves this browser\'s copy in place. ' +
      '<a href="#" data-act="export">Export a backup</a> &middot; <a href="#" data-act="import">Import</a> &middot; ' +
      '<a href="#" data-act="server">Change server</a></p>' +
      '<p class="account-foot"><a href="' + rel("account.html") + '">Open your area riservata &rarr;</a></p>';
  }

  function wirePanel() {
    var msg = panel.querySelector("#acctMsg");

    function say(text, cls) {
      if (!msg) return;
      msg.textContent = text || "";
      msg.className = "account-msg " + (cls || "");
    }

    var form = panel.querySelector("#acctForm");
    if (form) {
      form.addEventListener("submit", function (e) {
        e.preventDefault();
        var username = panel.querySelector("#acctUser").value.trim();
        var pass     = panel.querySelector("#acctPass").value;
        say("Working…");
        login(username, pass).then(function () {
          say("Signed in.", "ok");
          setTimeout(function () { closePanel(); openPanel(); }, 500);
        }).catch(function (err) {
          say(err.message || "Could not sign in.", "err");
        });
      });
    }

    panel.addEventListener("click", function (e) {
      var act = e.target.getAttribute && e.target.getAttribute("data-act");
      if (!act) return;
      e.preventDefault();

      if (act === "server") {
        var v = window.prompt(
          "Address of the LogiFlow Academy API.\n\n" +
          "Leave empty to work offline in this browser only.\n" +
          "Example: http://localhost:5280", base());
        if (v === null) return;
        setBase(v);
        closePanel();
        openPanel();
      }

      if (act === "logout") { logout().then(function () { closePanel(); openPanel(); }); }

      if (act === "syncnow") {
        say("Syncing…");
        sync(true).then(function (ok) { say(ok ? "Synced." : (state.message || "Up to date."), "ok"); });
      }

      if (act === "export") {
        var blob = new Blob([window.LF.exportJson()], { type: "application/json" });
        var a = document.createElement("a");
        a.href = URL.createObjectURL(blob);
        a.download = "logiflow-progress-" + window.LF.today() + ".json";
        document.body.appendChild(a);
        a.click();
        setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
      }

      if (act === "import") {
        var input = document.createElement("input");
        input.type = "file";
        input.accept = "application/json,.json";
        input.addEventListener("change", function () {
          var f = input.files && input.files[0];
          if (!f) return;
          var reader = new FileReader();
          reader.onload = function () {
            try {
              window.LF.importJson(String(reader.result));
              say("Imported.", "ok");
              setTimeout(function () { location.reload(); }, 600);
            } catch (err) {
              say("That file is not a LogiFlow profile.", "err");
            }
          };
          reader.readAsText(f);
        });
        input.click();
      }
    });
  }

  function addButton() {
    var bar = document.querySelector(".topbar");
    if (!bar) return;
    button = document.createElement("button");
    button.type = "button";
    button.className = "btn btn-account";
    bar.appendChild(button);
    button.addEventListener("click", openPanel);
    paint();
  }

  /* ────────────────────────────────────────────────────────────────────────
     SHOW THE ACCOUNT UI ONLY WHERE ACCOUNTS EXIST.

     The site deploys two ways. With the Academy API in front of it, /api/health
     answers and everything below works. As static assets — Cloudflare, Vercel,
     Netlify, Pages — there is no API, /api/* is a 404, and the button was still
     there: you could open the panel, type a password, and get "Request failed
     (404)", which reads as a broken site rather than as a feature that is not
     deployed here.

     A button that cannot work is worse than no button, so it is not added until
     /api/health answers.

     TWO THINGS THIS MUST NOT GET WRONG

     Anyone holding tokens has used accounts on this origin, so their button is
     shown unconditionally — hiding it would strand them with no way to sign out
     and no way to see whose progress the browser is holding. That check comes
     first and never touches the network.

     And the probe is remembered for the session, not forever. localStorage would
     mean deploying the API later leaves the button hidden until the entry aged
     out, and a single offline page load would hide it for everyone else.
     sessionStorage costs one 404 per tab and is always at most one visit stale.
     ──────────────────────────────────────────────────────────────────────── */
  var K_PROBE = "logiflow.api.present";

  function remember(v) {
    try { sessionStorage.setItem(K_PROBE, v); } catch (e) { /* private mode */ }
  }

  function accountsAvailable() {
    if (get(K_TOK) || get(K_REF)) return Promise.resolve(true);

    var seen = "";
    try { seen = sessionStorage.getItem(K_PROBE) || ""; } catch (e) { seen = ""; }
    if (seen) return Promise.resolve(seen === "1");

    /* A bare fetch, like health() below: /api/health is anonymous, and going
       through api() would attach a token and try to refresh one that is not
       there. The service worker passes /api/ straight to the network, so this
       is never answered from a cache. */
    return fetch(base() + "/api/health", { method: "GET" })
      .then(function (res) { return res.ok; })
      .catch(function () { return false; })
      .then(function (ok) { remember(ok ? "1" : "0"); return ok; });
  }

  accountsAvailable().then(function (ok) {
    if (!ok) return;
    addButton();
    if (state.status === "signed-in") setTimeout(function () { sync(false); }, 800);
  });

  window.LFAccount = {
    state: state,
    base: base,
    setBase: setBase,
    onChange: onChange,
    sync: sync,
    login: login,
    register: register,
    logout: logout,
    forgotPassword: forgotPassword,
    resetPassword: resetPassword,

    /**
     * Replace the account's recovery code with a new one, and return it.
     *
     * Goes through api() so an expired access token refreshes and retries once, and returns
     * the code to the caller rather than storing it anywhere. The old code stops working the
     * instant this resolves, so a page that fails to show the result has just cost somebody
     * their way back into the account.
     */
    newRecoveryCode: function () {
      return api("/api/auth/recovery-code", { method: "POST" })
        .then(function (payload) { return (payload && payload.recoveryCode) || ""; });
    },
    merge: merge,

    /** Drops this browser's tokens without telling the server. Local progress survives. */
    forget: function () { signOutLocal(); paint(); },

    /** The signed-in user, straight from the server rather than from this browser's copy. */
    me: function () { return api("/api/auth/me"); },

    /**
     * Is anybody home?
     *
     * A bare fetch on purpose: /api/health is anonymous, so going through api() would
     * attach an Authorization header and, on an expired token, spend a refresh round trip
     * before answering a question that has nothing to do with being signed in.
     */
    health: function () {
      var b = base();
      if (!b) return Promise.reject(new Error("No server address is set."));
      return fetch(b + "/api/health").then(function (res) {
        if (!res.ok) throw new Error("The server answered " + res.status + ".");
        return res.json();
      });
    },

    leaderboard: function () { return api("/api/leaderboard"); },

    /**
     * Show or hide this learner on the leaderboard.
     *
     * Goes through api() rather than a bare fetch() so it inherits the one thing that is
     * easy to forget: a 401 on an expired access token refreshes and retries once. A raw
     * fetch here would work all afternoon and then quietly stop after fifteen minutes.
     */
    setLeaderboardVisible: function (visible) {
      return api("/api/me/leaderboard?visible=" + (visible ? "true" : "false"), { method: "PUT" });
    },
  };
})();
