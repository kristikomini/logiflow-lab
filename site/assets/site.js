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
      /* First focusable thing on the page, and it has to be: the sidebar puts
         a search box and forty-seven chapter links ahead of the content, so
         without this a keyboard user tabs through the whole table of contents
         on every page before reaching a word of the chapter. Hidden until it
         takes focus — see .skip in style.css. */
      '<a class="skip" href="#main">Skip to the content</a>' +
      '<button class="btn hamburger" id="navToggle" aria-label="Chapters" ' +
        'aria-controls="sidebar" aria-expanded="false">&#9776;</button>' +
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

    /* The skip link needs somewhere to land, and every page already has the
       element — it just has no id, and <main> is not focusable on its own.
       Without tabindex the browser scrolls but leaves focus where it was, so
       the next Tab goes back into the sidebar and the link achieves nothing.
       Done here rather than in sixty-one files, which is the same reason the
       rest of this chrome is generated. */
    var main = document.querySelector("main.main");
    if (main) {
      if (!main.id) main.id = "main";
      main.setAttribute("tabindex", "-1");
    }
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
        // Someone who pressed "/" came here to type, so this is the one path
        // that does focus the field - see the note in wireNavToggle.
        setNav(true);
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
  /* The strip of browser furniture above the page - the status bar on a
     phone, the whole title bar once the site is installed to a home screen -
     is painted from <meta name="theme-color">. Each page ships two of them,
     one per colour scheme, which is right until the reader uses the toggle in
     the top bar: that sets data-theme on <html> and the media attribute on
     those metas knows nothing about it, so a page switched to light kept a
     dark bar sitting on top of it, and the seam was the first thing you saw.

     Both metas are given the resolved colour rather than trying to pick the
     matching one, since whichever the browser reads then says the same thing.
     The values are --panel from style.css: the top bar is what meets the
     browser's edge, and matching it is what makes the seam disappear. */
  function paintThemeColor(isDark) {
    var metas = document.querySelectorAll('meta[name="theme-color"]');
    for (var i = 0; i < metas.length; i++) {
      metas[i].setAttribute("content", isDark ? "#161925" : "#ffffff");
    }
  }

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
      paintThemeColor(next === "dark");
    });

    var cur = document.documentElement.getAttribute("data-theme");
    var isDark = cur === "dark" ||
      (!cur && window.matchMedia("(prefers-color-scheme: dark)").matches);
    btn.innerHTML = isDark ? "&#9788;" : "&#9789;";
    // Only when there is a saved choice to honour. With none, the pair of
    // media-scoped metas in the page is already correct and following the
    // system on its own, and overwriting both would freeze it at whatever the
    // system said at load.
    if (cur) paintThemeColor(cur === "dark");
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

  /* ------------------------------------------------------------ nav drawer
     Below NAV_BREAKPOINT the sidebar stops being a column of the layout and
     becomes an off-canvas drawer over the page. That is a different component
     with different obligations - a way out that is not the button you came in
     by, a state the screen reader can read, and a page underneath that does
     not scroll away while you are looking at the menu - so the state is set in
     one place here rather than by whoever happens to want it moved.

     The number is duplicated from the media query in style.css. It has to be:
     CSS decides what the drawer looks like and this decides how it behaves,
     and there is no way to ask the stylesheet. Changing one means changing the
     other. */
  var NAV_BREAKPOINT = 980;

  function navIsDrawer() {
    return window.matchMedia("(max-width: " + NAV_BREAKPOINT + "px)").matches;
  }

  function setNav(open) {
    var btn = document.getElementById("navToggle");
    body.setAttribute("data-nav", open ? "open" : "closed");
    if (btn) btn.setAttribute("aria-expanded", open ? "true" : "false");
  }

  function closeNav(returnFocus) {
    if (body.getAttribute("data-nav") !== "open") return;
    setNav(false);
    /* Only when the drawer was dismissed by a key. A tap that lands on the
       page has already said where the reader's attention went, and pulling
       focus back to the hamburger after it would scroll the bar into view and
       fight them for it. */
    if (returnFocus) {
      var btn = document.getElementById("navToggle");
      if (btn) btn.focus();
    }
  }

  function wireNavToggle() {
    var btn   = document.getElementById("navToggle");
    var aside = document.getElementById("sidebar");
    if (!btn) return;

    /* The scrim is built here rather than written into every page: it exists
       only because this script turns the sidebar into a drawer, so it belongs
       to the same piece of code. aria-hidden because it is decoration - the
       way out it offers is also on Escape and on the button. */
    var scrim = document.createElement("div");
    scrim.className = "nav-scrim";
    scrim.setAttribute("aria-hidden", "true");
    body.appendChild(scrim);

    if (aside) {
      // Somewhere for focus to land that is not a form field. See below.
      aside.setAttribute("tabindex", "-1");
      aside.setAttribute("aria-label", "Chapters");
    }

    btn.addEventListener("click", function () {
      var opening = body.getAttribute("data-nav") !== "open";
      setNav(opening);
      /* Focus moves to the panel, deliberately not to the search box inside
         it. Focusing the search field is the obvious thing to do and it is
         wrong on a phone: it raises the on-screen keyboard, which covers the
         bottom half of the list of chapters the reader just asked to see. The
         panel takes focus instead - enough for a screen reader to be reading
         from inside the drawer, and for the next Tab to reach the search box
         for anyone who does want it. The "/" shortcut in wireSearch is the
         path for people who came to search, and that one still focuses it. */
      if (opening && aside) aside.focus();
    });

    // Tapping the page behind the drawer is the way out most people reach for
    // first, and before this there wasn't one - the drawer could only be shut
    // by finding the same small button again.
    scrim.addEventListener("click", function () { closeNav(false); });

    // Following a link closes it, because the drawer is now over the page that
    // link just went to.
    document.addEventListener("click", function (e) {
      if (e.target.closest && e.target.closest(".sidebar a") && navIsDrawer()) {
        closeNav(false);
      }
    });

    document.addEventListener("keydown", function (e) {
      if (e.key !== "Escape" || !navIsDrawer()) return;
      /* Escape inside a search box with something in it means "clear the box"
         - wireSearch handles that - and should not also close the drawer out
         from under the reader. Once the box is empty, Escape means the same
         thing here as it does everywhere else. */
      var input = document.getElementById("navSearch");
      if (input && document.activeElement === input && input.value) return;
      closeNav(true);
    });

    /* Swiping the drawer away. The guard is the whole trick: the drawer is a
       tall scrolling list, so a touch that moves is usually someone scrolling
       it, and a swipe-to-close that fires on those makes the list unusable.
       The first move decides - more vertical than horizontal and the gesture
       is not ours - and only then does a leftward run of 45px close it. */
    if (aside) {
      var sx = 0, sy = 0, tracking = false;
      aside.addEventListener("touchstart", function (e) {
        tracking = navIsDrawer() && e.touches.length === 1;
        if (!tracking) return;
        sx = e.touches[0].clientX;
        sy = e.touches[0].clientY;
      }, { passive: true });
      aside.addEventListener("touchmove", function (e) {
        if (!tracking) return;
        var dx = e.touches[0].clientX - sx;
        var dy = e.touches[0].clientY - sy;
        if (Math.abs(dy) > Math.abs(dx)) { tracking = false; return; }
        if (dx < -45) { tracking = false; closeNav(false); }
      }, { passive: true });
      aside.addEventListener("touchend", function () { tracking = false; }, { passive: true });
    }

    /* Turning the phone sideways can cross the breakpoint, at which point the
       sidebar is a column again and "open" is not a state it has. Left set,
       the button would go on announcing itself as expanded and the scrim would
       be waiting to reappear on the next rotation back. */
    window.addEventListener("resize", function () {
      if (!navIsDrawer()) setNav(false);
    });

    setNav(false);
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
