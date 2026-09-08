/* ==========================================================================
   learn.js — everything the learner sees about their own progress.

   Runs on every page, after site.js has built the chrome. It:

     * adds the progress pill, streak and review badge to the top bar
     * adds the study links and per-chapter status marks to the sidebar
     * mounts the end-of-chapter test on chapter pages
     * decides when a chapter counts as "read"
     * shows XP and badge toasts

   It never assumes an element exists. Any page can leave a hook out and the
   rest still works, which is what keeps 53 hand-written chapter files from
   becoming 51 things that can break.
   ========================================================================== */
(function () {
  "use strict";

  if (!window.LF) return;

  var body    = document.body;
  var current = body.getAttribute("data-chapter") || "";
  var atRoot  = !current;
  var rel     = function (f) { return (atRoot ? "" : "../") + f; };

  function esc(s) {
    return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;")
      .replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  function el(tag, className, html) {
    var n = document.createElement(tag);
    if (className) n.className = className;
    if (html !== undefined) n.innerHTML = html;
    return n;
  }

  /* ======================================================================
     A progress ring, as inline SVG. Used in the top bar and the dashboard.
     ====================================================================== */

  function ring(percent, size, stroke) {
    var s = size || 26, w = stroke || 3.5;
    var r = (s - w) / 2;
    var c = 2 * Math.PI * r;
    var filled = Math.max(0, Math.min(100, percent)) / 100 * c;
    return '<svg class="ring" viewBox="0 0 ' + s + " " + s + '" width="' + s + '" height="' + s + '" aria-hidden="true">' +
      '<circle cx="' + s / 2 + '" cy="' + s / 2 + '" r="' + r + '" fill="none" stroke-width="' + w + '" class="ring-track"/>' +
      '<circle cx="' + s / 2 + '" cy="' + s / 2 + '" r="' + r + '" fill="none" stroke-width="' + w + '" class="ring-fill" ' +
      'stroke-dasharray="' + filled.toFixed(2) + " " + (c - filled).toFixed(2) + '" ' +
      'stroke-linecap="round" transform="rotate(-90 ' + s / 2 + " " + s / 2 + ')"/>' +
      "</svg>";
  }

  /* ======================================================================
     Top bar
     ====================================================================== */

  var pill, streakChip, dueChip;

  function augmentTopbar() {
    var bar = document.querySelector(".topbar");
    if (!bar) return;
    var spacer = bar.querySelector(".spacer");
    if (!spacer) return;

    pill = el("a", "chip chip-progress");
    pill.href = rel("dashboard.html");
    pill.title = "Course mastery — click for the dashboard";

    streakChip = el("a", "chip chip-streak");
    streakChip.href = rel("dashboard.html");
    streakChip.title = "Study streak";

    dueChip = el("a", "chip chip-due");
    dueChip.href = rel("review.html");
    dueChip.title = "Cards due for review";

    spacer.parentNode.insertBefore(dueChip, spacer.nextSibling);
    spacer.parentNode.insertBefore(streakChip, spacer.nextSibling);
    spacer.parentNode.insertBefore(pill, spacer.nextSibling);

    paintTopbar();
  }

  function paintTopbar() {
    if (!pill) return;
    var m = window.LF.mastery();
    var lv = window.LF.level();
    pill.innerHTML = ring(m.percent, 22, 3) +
      '<span class="chip-num">' + m.percent + "%</span>";
    pill.title = "Course mastery " + m.percent + "% · " + lv.name +
      " · " + window.LF.profile.xp + " XP";

    var s = window.LF.profile.streak;
    var alive = window.LF.streakAlive();
    streakChip.hidden = !s.count;
    streakChip.className = "chip chip-streak" + (alive ? "" : " cold");
    streakChip.innerHTML = "🔥 " + s.count;
    streakChip.title = alive
      ? "Streak: " + s.count + " day" + (s.count === 1 ? "" : "s") + " (best " + s.best + ")"
      : "Streak broken — best was " + s.best;

    var n = window.LF.due().length;
    dueChip.hidden = !n;
    dueChip.innerHTML = "🗂 " + n;
    dueChip.title = n + " card" + (n === 1 ? "" : "s") + " due for review";
  }

  /* ======================================================================
     Sidebar: study links, per-chapter marks, part bars
     ====================================================================== */

  var STUDY_LINKS = [
    { file: "dashboard.html", icon: "📊", label: "Dashboard" },
    { file: "review.html",    icon: "🗂",  label: "Review",   badge: "due" },
    { file: "viva.html",      icon: "🗣", label: "Viva",     badge: "viva" },
    { file: "exam.html",      icon: "📝", label: "Mock exam" },
    { file: "simulate.html",  icon: "⏱", label: "Simulator" },
    { file: "notes.html",     icon: "📌", label: "My notes",  badge: "notes" },
    { file: "glossary.html",  icon: "🔤", label: "Glossary" },
    { file: "italiano.html",  icon: "🇮🇹", label: "In italiano" },
    { file: "cv.html",        icon: "📄", label: "CV e lettera" },
    { file: "account.html",   icon: "🔐", label: "Area riservata" },
  ];

  function augmentSidebar() {
    var aside = document.getElementById("sidebar");
    if (!aside) return;

    var here = (location.pathname.split("/").pop() || "index.html");
    var block = el("nav", "study-nav");
    var html = '<div class="nav-part">Your study</div>';
    STUDY_LINKS.forEach(function (l) {
      html += '<a class="study-link' + (here === l.file ? " current" : "") +
        '" href="' + rel(l.file) + '" data-badge="' + (l.badge || "") + '">' +
        '<span class="study-icon">' + l.icon + "</span><span>" + l.label + "</span>" +
        '<span class="study-count"></span></a>';
    });
    block.innerHTML = html;
    aside.insertBefore(block, aside.firstChild);

    markChapters();
    paintSidebarCounts();
  }

  function paintSidebarCounts() {
    var due = window.LF.due().length;
    var viva = window.LF.vivaDue().length;
    var notes = window.LF.profile.notes.length;
    var nodes = document.querySelectorAll(".study-link");
    for (var i = 0; i < nodes.length; i++) {
      var kind = nodes[i].getAttribute("data-badge");
      var span = nodes[i].querySelector(".study-count");
      var value = kind === "due" ? due : kind === "viva" ? viva : kind === "notes" ? notes : 0;
      span.textContent = value ? value : "";
      span.hidden = !value;
    }
  }

  /** A tick, a half-tick or nothing beside every chapter in the sidebar. */
  function markChapters() {
    var links = document.querySelectorAll(".sidebar a.nav-link");
    var list = window.CHAPTERS || [];
    for (var i = 0; i < links.length; i++) {
      var href = links[i].getAttribute("href") || "";
      var id = href.split("/").pop().replace(/\.html$/, "");
      var st = window.LF.profile.chapters[id];
      var mark = links[i].querySelector(".nav-state");
      if (!mark) {
        mark = el("span", "nav-state");
        links[i].appendChild(mark);
      }
      if (!st) { mark.textContent = ""; mark.title = ""; continue; }
      if (st.best >= 1)        { mark.textContent = "★"; mark.title = "Perfect score"; mark.className = "nav-state ace"; }
      else if (st.best >= 0.8) { mark.textContent = "✔"; mark.title = "Test passed (" + Math.round(st.best * 100) + "%)"; mark.className = "nav-state pass"; }
      else if (st.best > 0)    { mark.textContent = "◐"; mark.title = "Best so far " + Math.round(st.best * 100) + "%"; mark.className = "nav-state part"; }
      else if (st.read)        { mark.textContent = "·"; mark.title = "Read, not yet tested"; mark.className = "nav-state read"; }
      else                     { mark.textContent = ""; }
    }
    if (list.length) { /* keeps the reference explicit for readers */ }
  }

  /* ======================================================================
     Toasts
     ====================================================================== */

  var toastHost;

  function toast(html, kind, ms) {
    if (!toastHost) {
      toastHost = el("div", "toasts");
      document.body.appendChild(toastHost);
    }
    var t = el("div", "toast " + (kind || ""), html);
    toastHost.appendChild(t);
    // Force a reflow so the transition runs from the initial state.
    void t.offsetWidth;
    t.classList.add("in");
    setTimeout(function () {
      t.classList.remove("in");
      setTimeout(function () { if (t.parentNode) t.remove(); }, 320);
    }, ms || 3200);
  }

  var xpBuffer = 0, xpTimer = null;

  window.LF.on("xp", function (e) {
    xpBuffer += e.amount;
    if (xpTimer) clearTimeout(xpTimer);
    // Coalesce: finishing a ten-question test should be one toast, not ten.
    xpTimer = setTimeout(function () {
      var n = xpBuffer; xpBuffer = 0; xpTimer = null;
      if (n > 0) toast('<span class="toast-xp">+' + n + " XP</span>", "xp", 2000);
      paintTopbar();
    }, 700);
  });

  window.LF.on("badges", function (list) {
    list.forEach(function (b, i) {
      setTimeout(function () {
        toast('<span class="toast-icon">' + b.icon + "</span>" +
              "<div><strong>" + esc(b.name) + "</strong><br>" +
              '<span class="subtle">' + esc(b.hint) + "</span></div>", "badge", 5200);
      }, i * 500);
    });
  });

  window.LF.on("change", function () {
    paintTopbar();
    paintSidebarCounts();
    markChapters();
  });

  window.LF.on("replaced", function () {
    paintTopbar();
    paintSidebarCounts();
    markChapters();
  });

  /* ======================================================================
     Chapter pages: reading credit and the end-of-chapter test
     ====================================================================== */

  /**
   * A chapter counts as read when the learner has actually reached the end of
   * it, or has had it open for two minutes. Neither is proof of anything —
   * which is exactly why reading is worth only a quarter of a chapter's
   * mastery score and the test is worth the rest.
   */
  function trackReading() {
    if (!current) return;
    if (window.LF.profile.chapters[current] && window.LF.profile.chapters[current].read) return;

    var done = false;
    function credit() {
      if (done) return;
      done = true;
      window.removeEventListener("scroll", onScroll);
      window.LF.markRead(current);
    }
    function onScroll() {
      var reachedEnd = window.scrollY + window.innerHeight >=
        document.documentElement.scrollHeight - 400;
      if (reachedEnd) credit();
    }
    window.addEventListener("scroll", onScroll, { passive: true });
    setTimeout(credit, 120000);
    onScroll();   // a short chapter on a tall screen is already finished
  }

  function mountChapterTest() {
    if (!current || !window.LFQuiz) return;

    var host = document.getElementById("chapter-quiz");
    if (!host) {
      // Chapters written before this feature existed have no hook. Put the
      // test just above the prev/next pager, which every chapter has.
      var pager = document.getElementById("pager");
      if (!pager) return;
      host = el("section");
      host.id = "chapter-quiz";
      pager.parentNode.insertBefore(host, pager);
    }

    var questions = window.LFQuiz.forChapter(current);
    if (!questions.length) {
      host.innerHTML = "";
      return;
    }

    var st = window.LF.profile.chapters[current] || {};
    var best = st.best ? Math.round(st.best * 100) : 0;

    host.innerHTML =
      '<h2 id="test-yourself">Test yourself</h2>' +
      '<div class="quiz-intro">' +
        "<p>" + questions.length + " questions on this chapter. " +
        "You get the explanation either way, and anything you miss goes into your " +
        '<a href="' + rel("review.html") + '">review deck</a> automatically.</p>' +
        (best ? '<p class="quiz-best">Your best so far: <strong>' + best + "%</strong>" +
                (st.attempts ? " over " + st.attempts + " attempt" + (st.attempts === 1 ? "" : "s") : "") +
                ".</p>" : "") +
        '<button type="button" class="btn primary big" data-act="start">' +
          (best ? "Take it again" : "Start the test") + "</button>" +
      "</div>" +
      '<div id="chapter-quiz-mount"></div>';

    host.querySelector('[data-act="start"]').addEventListener("click", function () {
      var intro = host.querySelector(".quiz-intro");
      intro.hidden = true;
      window.LFQuiz.mount(document.getElementById("chapter-quiz-mount"), {
        questions: questions,
        mode: "test",
        chapter: current,
        title: "End-of-chapter test",
        subtitle: "Answer from memory. Looking it up teaches you nothing.",
        onFinish: function () {
          intro.hidden = false;
          var again = intro.querySelector('[data-act="start"]');
          if (again) again.textContent = "Take it again";
          var b = window.LF.profile.chapters[current];
          var line = intro.querySelector(".quiz-best");
          if (b && b.best) {
            if (!line) {
              line = el("p", "quiz-best");
              intro.insertBefore(line, intro.querySelector("button"));
            }
            line.innerHTML = "Your best so far: <strong>" + Math.round(b.best * 100) + "%</strong>" +
              " over " + b.attempts + " attempt" + (b.attempts === 1 ? "" : "s") + ".";
          }
        },
      });
      document.getElementById("chapter-quiz-mount").scrollIntoView({ behavior: "smooth", block: "start" });
    });
  }

  /* ======================================================================
     A "next thing to do" nudge, shown at the top of the home page
     ====================================================================== */

  function suggestion() {
    var host = document.getElementById("next-step");
    if (!host) return;

    var due = window.LF.due().length;
    var list = window.CHAPTERS || [];
    var nextUnread = null, nextUntested = null;
    for (var i = 0; i < list.length; i++) {
      var st = window.LF.profile.chapters[list[i].id];
      if (!st || !st.read) { if (!nextUnread) nextUnread = list[i]; }
      else if (st.best < 0.8) { if (!nextUntested) nextUntested = list[i]; }
    }

    var m = window.LF.mastery();
    var parts = [];

    if (due) {
      parts.push('<a class="next-card urgent" href="' + rel("review.html") + '">' +
        '<span class="next-kind">Review</span><strong>' + due + " card" + (due === 1 ? "" : "s") +
        " due today</strong><span class=\"subtle\">Ten minutes here is worth an hour of re-reading.</span></a>");
    }
    var vivaDue = window.LF.vivaDue().length;
    if (vivaDue) {
      parts.push('<a class="next-card" href="' + rel("viva.html") + '">' +
        '<span class="next-kind">Viva</span><strong>' + vivaDue + " rule" + (vivaDue === 1 ? "" : "s") +
        " to say out loud</strong><span class=\"subtle\">No options to pick from. This is the one that " +
        "matches the interview.</span></a>");
    }
    if (nextUntested) {
      parts.push('<a class="next-card" href="' + rel("chapters/" + nextUntested.id + ".html") + '#test-yourself">' +
        '<span class="next-kind">Unfinished</span><strong>' + esc(nextUntested.n + " · " + nextUntested.title) +
        "</strong><span class=\"subtle\">Read, but the test is not passed yet.</span></a>");
    }
    if (nextUnread) {
      parts.push('<a class="next-card" href="' + rel("chapters/" + nextUnread.id + ".html") + '">' +
        '<span class="next-kind">Next chapter</span><strong>' + esc(nextUnread.n + " · " + nextUnread.title) +
        "</strong><span class=\"subtle\">" + esc(nextUnread.blurb) + "</span></a>");
    }
    if (!parts.length) {
      parts.push('<a class="next-card" href="' + rel("exam.html") + '">' +
        '<span class="next-kind">Everything is passed</span><strong>Sit the mock exam</strong>' +
        "<span class=\"subtle\">Mixed questions from all " + list.length + " chapters, timed.</span></a>");
    }

    host.className = "next-steps";
    host.innerHTML =
      '<div class="next-head"><div>' + ring(m.percent, 46, 5) + "</div>" +
        "<div><strong>" + m.percent + "% of the course mastered</strong>" +
        '<div class="subtle">' + m.read + " of " + m.total + " chapters read · " +
        m.tested + " passed · " + window.LF.profile.xp + " XP · " + window.LF.level().name + "</div></div></div>" +
      '<div class="next-grid">' + parts.slice(0, 3).join("") + "</div>";
  }

  /* ======================================================================
     Keyboard: g then d/r/e/n jumps to a study page
     ====================================================================== */

  function shortcuts() {
    var armed = false, timer = null;
    document.addEventListener("keydown", function (e) {
      var t = e.target.tagName;
      if (t === "INPUT" || t === "TEXTAREA" || e.metaKey || e.ctrlKey || e.altKey) return;
      if (e.target.isContentEditable) return;

      if (!armed) {
        if (e.key === "g") {
          armed = true;
          timer = setTimeout(function () { armed = false; }, 1200);
        }
        return;
      }
      armed = false;
      if (timer) clearTimeout(timer);
      var map = { d: "dashboard.html", r: "review.html", e: "exam.html", n: "notes.html",
                  l: "glossary.html", a: "account.html", h: "index.html", v: "viva.html",
                  s: "simulate.html", i: "italiano.html", c: "cv.html" };
      if (map[e.key]) { e.preventDefault(); location.href = rel(map[e.key]); }
    });
  }

  /* ---------------------------------------------------------------- go */

  augmentTopbar();
  augmentSidebar();
  trackReading();
  mountChapterTest();
  suggestion();
  shortcuts();

  window.LFLearn = { ring: ring, toast: toast, esc: esc, rel: rel, repaint: paintTopbar };
})();
