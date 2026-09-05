/* ==========================================================================
   site.js — builds the chrome that every page shares.

   Deliberately plain: no framework, no bundler, no fetch(). Every page is a
   real .html file, so this works when you double-click index.html from disk
   (file://), which XHR-based approaches do not.
   ========================================================================== */
(function () {
  "use strict";

  var body    = document.body;
  var current = body.getAttribute("data-chapter") || "";
  // Chapter pages live in /chapters; every other page (home, dashboard,
  // review, exam, notes, glossary) sits at the site root.
  var isHome  = !current;
  var toChap  = function (id) { return (isHome ? "chapters/" : "") + id + ".html"; };
  var toHome  = isHome ? "index.html" : "../index.html";
  var toRoot  = function (file) { return (isHome ? "" : "../") + file; };

  /* ---------------------------------------------------------------- storage
     localStorage throws in some embedded/private contexts. Never let a
     preference lookup take the page down. */
  function pref(key, fallback) {
    try { return localStorage.getItem(key) || fallback; } catch (e) { return fallback; }
  }
  function savePref(key, value) {
    try { localStorage.setItem(key, value); } catch (e) { /* ignore */ }
  }

  /* ------------------------------------------------------------------ chrome */
  function buildTopbar() {
    var bar = document.createElement("header");
    bar.className = "topbar";
    bar.innerHTML =
      '<button class="btn hamburger" id="navToggle" aria-label="Menu">&#9776;</button>' +
      '<a class="brand" href="' + toHome + '">' +
        '<span class="brand-mark">C#</span>' +
        '<span class="brand-text">LogiFlow Academy</span>' +
      "</a>" +
      '<span class="brand-sub">.NET developer &mdash; Emilia-Romagna</span>' +
      '<span class="spacer"></span>' +
      '<div class="segmented" id="modeSwitch" role="group" aria-label="Explanation level">' +
        '<button data-mode="both" title="Show both explanations">Both</button>' +
        '<button data-mode="kid"  title="Only the simple explanation">Simple</button>' +
        '<button data-mode="pro"  title="Only the professional explanation">Pro</button>' +
      "</div>" +
      '<button class="btn" id="themeToggle" aria-label="Toggle dark mode">&#9789;</button>';
    body.insertBefore(bar, body.firstChild);
  }

  function buildSidebar() {
    var aside = document.getElementById("sidebar");
    if (!aside) return;

    var html = '<div class="search-wrap">' +
      '<input type="search" id="navSearch" placeholder="Search chapters&hellip;  (press /)" ' +
      'autocomplete="off" spellcheck="false"></div><div id="navList"></div>';
    aside.innerHTML = html;

    var list = aside.querySelector("#navList");
    var lastPart = null;
    var out = [];

    window.CHAPTERS.forEach(function (c) {
      if (c.part !== lastPart) {
        out.push('<div class="nav-part" data-part="1">' + c.part + "</div>");
        lastPart = c.part;
      }
      var cls = "nav-link" + (c.id === current ? " current" : "");
      out.push(
        '<a class="' + cls + '" href="' + toChap(c.id) + '" ' +
        'data-search="' + (c.n + " " + c.title + " " + c.blurb + " " + c.tags).toLowerCase() + '">' +
        '<span class="num">' + c.n + "</span><span>" + c.title + "</span></a>"
      );
    });
    list.innerHTML = out.join("");

    // Keep the active chapter visible when the list is long.
    var active = list.querySelector(".current");
    if (active) {
      var top = active.offsetTop - aside.clientHeight / 2;
      if (top > 0) aside.scrollTop = top;
    }
  }

  /* ------------------------------------------------------------------ search */
  function wireSearch() {
    var input = document.getElementById("navSearch");
    var list  = document.getElementById("navList");
    if (!input || !list) return;

    input.addEventListener("input", function () {
      var q = input.value.trim().toLowerCase();
      var links = list.querySelectorAll("a.nav-link");
      var parts = list.querySelectorAll(".nav-part");
      var hits  = 0;

      if (!q) {
        for (var i = 0; i < links.length; i++) links[i].hidden = false;
        for (var p = 0; p < parts.length; p++) parts[p].hidden = false;
        var empty = list.querySelector(".nav-empty");
        if (empty) empty.remove();
        return;
      }

      for (var j = 0; j < links.length; j++) {
        var match = links[j].getAttribute("data-search").indexOf(q) !== -1;
        links[j].hidden = !match;
        if (match) hits++;
      }
      // Hide a part heading when everything under it is hidden.
      for (var k = 0; k < parts.length; k++) {
        var node = parts[k].nextElementSibling, any = false;
        while (node && !node.classList.contains("nav-part")) {
          if (node.classList.contains("nav-link") && !node.hidden) { any = true; break; }
          node = node.nextElementSibling;
        }
        parts[k].hidden = !any;
      }

      var msg = list.querySelector(".nav-empty");
      if (!hits && !msg) {
        msg = document.createElement("div");
        msg.className = "nav-empty";
        msg.textContent = "No chapter matches that.";
        list.appendChild(msg);
      } else if (hits && msg) {
        msg.remove();
      }
    });

    document.addEventListener("keydown", function (e) {
      if (e.key === "/" && document.activeElement !== input) {
        e.preventDefault();
        body.setAttribute("data-nav", "open");
        input.focus();
        input.select();
      }
      if (e.key === "Escape" && document.activeElement === input) {
        input.value = "";
        input.dispatchEvent(new Event("input"));
        input.blur();
      }
    });
  }

  /* ------------------------------------------------- theme + explanation mode */
  function wireTheme() {
    var btn = document.getElementById("themeToggle");
    var saved = pref("logiflow-theme", "");
    if (saved) document.documentElement.setAttribute("data-theme", saved);

    btn.addEventListener("click", function () {
      var now = document.documentElement.getAttribute("data-theme");
      if (!now) {
        // No explicit choice yet: flip away from whatever the system gives us.
        var dark = window.matchMedia("(prefers-color-scheme: dark)").matches;
        now = dark ? "dark" : "light";
      }
      var next = now === "dark" ? "light" : "dark";
      document.documentElement.setAttribute("data-theme", next);
      savePref("logiflow-theme", next);
      btn.innerHTML = next === "dark" ? "&#9788;" : "&#9789;";
    });

    var cur = document.documentElement.getAttribute("data-theme");
    var isDark = cur === "dark" ||
      (!cur && window.matchMedia("(prefers-color-scheme: dark)").matches);
    btn.innerHTML = isDark ? "&#9788;" : "&#9789;";
  }

  function wireMode() {
    var group = document.getElementById("modeSwitch");
    var mode  = pref("logiflow-mode", "both");
    apply(mode);

    group.addEventListener("click", function (e) {
      var b = e.target.closest("button[data-mode]");
      if (!b) return;
      apply(b.getAttribute("data-mode"));
      savePref("logiflow-mode", b.getAttribute("data-mode"));
    });

    function apply(m) {
      body.setAttribute("data-mode", m);
      var buttons = group.querySelectorAll("button");
      for (var i = 0; i < buttons.length; i++) {
        buttons[i].setAttribute("aria-pressed",
          buttons[i].getAttribute("data-mode") === m ? "true" : "false");
      }
    }
  }

  function wireNavToggle() {
    var btn = document.getElementById("navToggle");
    btn.addEventListener("click", function () {
      body.setAttribute("data-nav", body.getAttribute("data-nav") === "open" ? "closed" : "open");
    });
    // Tapping a link on mobile should close the drawer.
    document.addEventListener("click", function (e) {
      if (e.target.closest && e.target.closest(".sidebar a") && window.innerWidth <= 980) {
        body.setAttribute("data-nav", "closed");
      }
    });
  }

  /* ----------------------------------------------------------- copy buttons */
  function wireCopy() {
    var heads = document.querySelectorAll(".code-head");
    for (var i = 0; i < heads.length; i++) {
      (function (head) {
        var btn = document.createElement("button");
        btn.className = "copy";
        btn.type = "button";
        btn.textContent = "copy";
        head.appendChild(btn);
        btn.addEventListener("click", function () {
          var pre = head.nextElementSibling;
          if (!pre) return;
          var text = pre.innerText;
          var done = function () {
            btn.textContent = "copied";
            setTimeout(function () { btn.textContent = "copy"; }, 1200);
          };
          if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(done, fallback);
          } else { fallback(); }

          function fallback() {
            var ta = document.createElement("textarea");
            ta.value = text;
            ta.style.position = "fixed";
            ta.style.opacity = "0";
            document.body.appendChild(ta);
            ta.select();
            try { document.execCommand("copy"); done(); } catch (e) { /* ignore */ }
            ta.remove();
          }
        });
      })(heads[i]);
    }
  }

  /* ----------------------------------------------------- on-this-page + pager */
  function buildToc() {
    var host = document.getElementById("toc");
    if (!host) return;
    var hs = document.querySelectorAll(".main h2");
    if (hs.length < 3) { host.remove(); return; }

    var items = [];
    for (var i = 0; i < hs.length; i++) {
      if (!hs[i].id) {
        hs[i].id = hs[i].textContent.toLowerCase()
          .replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
      }
      items.push('<li><a href="#' + hs[i].id + '">' + hs[i].textContent + "</a></li>");
    }
    host.className = "box";
    host.innerHTML = '<div class="box-title">On this page</div><ul>' + items.join("") + "</ul>";
  }

  function buildPager() {
    var host = document.getElementById("pager");
    if (!host) return;
    var idx = -1;
    for (var i = 0; i < window.CHAPTERS.length; i++) {
      if (window.CHAPTERS[i].id === current) { idx = i; break; }
    }
    if (idx === -1) return;

    var prev = idx > 0 ? window.CHAPTERS[idx - 1] : null;
    var next = idx < window.CHAPTERS.length - 1 ? window.CHAPTERS[idx + 1] : null;
    var html = "";

    html += prev
      ? '<a class="prev" href="' + toChap(prev.id) + '"><span class="dir">&#8592; Previous</span>' +
        '<span class="ttl">' + prev.n + " &middot; " + prev.title + "</span></a>"
      : '<a class="prev" href="' + toHome + '"><span class="dir">&#8592; Previous</span>' +
        '<span class="ttl">Home</span></a>';

    html += next
      ? '<a class="next" href="' + toChap(next.id) + '"><span class="dir">Next &#8594;</span>' +
        '<span class="ttl">' + next.n + " &middot; " + next.title + "</span></a>"
      : '<a class="next" href="' + toHome + '"><span class="dir">Next &#8594;</span>' +
        '<span class="ttl">Back to the contents</span></a>';

    host.className = "pager";
    host.innerHTML = html;

    // Arrow keys move between chapters, unless the user is typing.
    document.addEventListener("keydown", function (e) {
      var t = e.target.tagName;
      if (t === "INPUT" || t === "TEXTAREA" || e.metaKey || e.ctrlKey || e.altKey) return;
      if (e.key === "ArrowLeft"  && prev) location.href = toChap(prev.id);
      if (e.key === "ArrowRight" && next) location.href = toChap(next.id);
    });
  }

  /* ------------------------------------------------------- home-page cards */
  function buildCards() {
    var host = document.getElementById("cards");
    if (!host) return;
    var lastPart = null, out = [];
    window.CHAPTERS.forEach(function (c) {
      if (c.part !== lastPart) {
        if (lastPart !== null) out.push("</div>");
        out.push("<h2>" + c.part + '</h2><div class="grid">');
        lastPart = c.part;
      }
      out.push(
        '<a class="card" href="' + toChap(c.id) + '">' +
          '<span class="card-num">CHAPTER ' + c.n + "</span>" +
          "<h3>" + c.title + "</h3><p>" + c.blurb + "</p></a>"
      );
    });
    out.push("</div>");
    host.innerHTML = out.join("");
  }

  /* --------------------------------------------------------------- coverage */
  function coverageTable(hostId, field, heading) {
    var host = document.getElementById(hostId);
    if (!host) return;
    var rows = window.CHAPTERS.filter(function (c) {
      return c[field];
    }).map(function (c) {
      return "<tr><td>" + c[field] + '</td><td><a href="' + toChap(c.id) + '">' +
        c.n + " &middot; " + c.title + "</a></td></tr>";
    }).join("");
    host.innerHTML =
      '<div class="table-wrap"><table><thead><tr><th>' + heading +
      "</th><th>Where it is covered</th>" +
      "</tr></thead><tbody>" + rows + "</tbody></table></div>";
  }

  function buildCoverage() {
    coverageTable("coverage-table", "req", "Line in the advert");
    coverageTable("beyond-table", "extra", "Why it is here anyway");
  }

  /* ------------------------------------------------------------- offline
     Registers the service worker, and shows a small badge when the page is
     being served without a network.

     TWO GUARDS, both load-bearing:

       * `serviceWorker in navigator` — absent in some embedded webviews.
       * `location.protocol` — service workers are refused over file://, and
         the rejection is an unhandled promise error in the console that looks
         like a bug in this site. Opening index.html by double-clicking is a
         documented, supported way to use this tutorial, so it must be silent. */
  function wireOffline() {
    if (location.protocol === "file:" || !("serviceWorker" in navigator)) return;

    navigator.serviceWorker.register(toRoot("sw.js")).catch(function (err) {
      console.warn("Offline support unavailable:", err);
    });

    var badge = document.createElement("div");
    badge.className = "offline-badge";
    badge.textContent = "Offline — reading from this device";
    badge.hidden = navigator.onLine;
    document.body.appendChild(badge);

    var paint = function () { badge.hidden = navigator.onLine; };
    window.addEventListener("online", paint);
    window.addEventListener("offline", paint);
  }

  /* -------------------------------------------------------------------- go */
  buildTopbar();
  buildSidebar();
  wireSearch();
  wireTheme();
  wireMode();
  wireNavToggle();
  wireCopy();
  buildToc();
  buildPager();
  buildCards();
  buildCoverage();
  wireOffline();
})();
