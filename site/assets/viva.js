/* ==========================================================================
   viva.js — the free-recall drill.

   Every other retrieval surface on this site is multiple choice: the
   end-of-chapter test, the review deck, the mock exam. Recognition is cheap to
   grade and it is genuinely useful, but it is not the thing an interview asks
   for. "Spiegami perché" has no four options underneath it. You have to
   PRODUCE the sentence, in order, out loud, in a language that may not be your
   first — and it is entirely possible to sit at 90% mastery on this site and
   still not be able to do that, because nothing here has ever asked you to.

   This page asks. It draws a Golden rule, shows you the claim, and gives you
   nothing else until you have committed an answer.

   THREE DESIGN DECISIONS WORTH ARGUING WITH
   =========================================

   1. You cannot see the answer until you have written one — or pressed
      "I can't say it", which grades itself 0 before revealing anything.
      Retrieval only strengthens a memory when it is actually attempted; a
      button that shows you the model answer while you are still thinking
      converts the exercise back into reading, which is where you started.

   2. You mark yourself, which is the weakest part of the design and is
      mitigated rather than solved. Two mitigations: your own answer stays on
      screen next to the written one, so you are comparing texts rather than
      recalling how confident you felt; and the rule's own code terms are
      checked against what you wrote and shown as hit or missed. That check is
      literal, not semantic — it cannot tell whether you were right — but
      "you never mentioned `rowversion`" is a fact, and facts are harder to
      mark generously than feelings.

   3. Typing is the default, not speaking, even though the interview is spoken.
      Writing forces the same production and leaves something to compare
      against. Speaking is one toggle away for anyone who wants the harder,
      more realistic version, and then the comparison is honest self-report.

   The deck itself is generated from course/GOLDEN-RULES.md — see rules.js and
   tools/viva-deck.cs. The scheduler is the site's own SM-2, in its own map:
   window.LF.vivaReview / vivaDue / vivaStats.
   ========================================================================== */
(function () {
  "use strict";

  if (!window.LF || !window.RULES) return;

  var LF = window.LF;
  var RULES = window.RULES;

  /* ---------------------------------------------------------------- utils */

  function esc(s) {
    return String(s)
      .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  /**
   * The deck stores the course's own markdown, because that is what the rules
   * are written in and stripping it would lose `IQueryable` as a code term.
   * Escaped FIRST, then the handful of inline forms are turned into elements —
   * so nothing in GOLDEN-RULES.md can ever inject markup into this page.
   */
  function md(text) {
    return esc(text)
      .replace(/`([^`]+)`/g, "<code>$1</code>")
      .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
      .replace(/\[([^\]]+)\]\(([^)]+)\)/g, "$1");
  }

  function shuffle(list) {
    var a = list.slice();
    for (var i = a.length - 1; i > 0; i--) {
      var j = Math.floor(Math.random() * (i + 1));
      var t = a[i]; a[i] = a[j]; a[j] = t;
    }
    return a;
  }

  /** Preferences are a convenience; a locked-down browser must not break the page. */
  function pref(key, fallback) {
    try { return window.localStorage.getItem("logiflow.viva." + key) || fallback; }
    catch (e) { return fallback; }
  }
  function savePref(key, value) {
    try { window.localStorage.setItem("logiflow.viva." + key, value); }
    catch (e) { /* memory-only is fine for a toggle */ }
  }

  /* ------------------------------------------------------------- the deck */

  var byId = {};
  RULES.forEach(function (r) { byId[r.id] = r; });

  function modules() {
    var seen = [], out = [];
    RULES.forEach(function (r) {
      if (r.tier !== "module" || seen.indexOf(r.module) !== -1) return;
      seen.push(r.module);
      out.push({ n: r.module, part: r.part, count: count(function (x) { return x.module === r.module; }) });
    });
    return out;
  }

  function count(fn) {
    var n = 0;
    RULES.forEach(function (r) { if (fn(r)) n++; });
    return n;
  }

  /** The eighteen the course says decide an interview. */
  function eighteen() {
    return RULES.filter(function (r) { return r.tier === "twelve" || r.tier === "senior"; });
  }

  function lapsed() {
    return RULES.filter(function (r) {
      var k = LF.profile.viva[r.id];
      return k && k.lapses > 0;
    });
  }

  function unseen() {
    return RULES.filter(function (r) { return !LF.profile.viva[r.id]; });
  }

  /**
   * Today's session. Due cards first — those are the ones the schedule says you
   * are about to forget — then new rules to fill the session out.
   *
   * The top-up matters more than it looks. On day one nothing is scheduled, so
   * a pure due-list would open on "nothing to do" in front of a learner holding
   * 384 unanswered rules. Introducing a few new ones every session is also how
   * the deck ever gets populated: unlike the quiz deck, which fills itself when
   * you take a chapter test, nothing else on this site schedules a rule.
   */
  function today(size) {
    var n = size || 15;
    var out = LF.vivaDue()
      .map(function (d) { return byId[d.id]; })
      // A rule that was drilled and has since been reworded is gone from the
      // deck but still in the profile. Skipping it is the whole handling.
      .filter(Boolean);

    if (out.length >= n) return out.slice(0, n);

    var fresh = shuffle(unseen());
    // New rules in course order once shuffled into a set, so a session moves
    // roughly forward through the material rather than jumping about.
    fresh = fresh.slice(0, n - out.length).sort(function (a, b) {
      return RULES.indexOf(a) - RULES.indexOf(b);
    });
    return out.concat(fresh);
  }

  /* ============================================================== the drill */

  /**
   * Mount a session.
   *
   * @param {Element} host
   * @param {object}  opts   { rules, title, onFinish }
   */
  function mount(host, opts) {
    if (!host) return null;
    var o = opts || {};
    var queue = (o.rules || []).slice();
    if (!queue.length) return null;

    var idx = 0;
    var grades = [];          // 0 | 1 | 2, per card
    var seconds = [];         // how long each card took
    var startedCard = 0;
    var ticker = null;

    host.className = "quiz viva";
    host.innerHTML =
      '<div class="quiz-head">' +
        '<div><div class="quiz-title"></div><div class="quiz-sub subtle"></div></div>' +
        '<div class="quiz-meta"><span class="quiz-count"></span><span class="quiz-clock"></span></div>' +
      "</div>" +
      '<div class="quiz-bar"><span></span></div>' +
      '<div class="quiz-body"></div>';

    var elTitle = host.querySelector(".quiz-title");
    var elSub   = host.querySelector(".quiz-sub");
    var elCount = host.querySelector(".quiz-count");
    var elClock = host.querySelector(".quiz-clock");
    var elBar   = host.querySelector(".quiz-bar span");
    var elBody  = host.querySelector(".quiz-body");

    elTitle.textContent = o.title || "Viva";
    elSub.textContent = speaking()
      ? "Say the answer out loud before you reveal it."
      : "Write the answer before you reveal it.";

    render();
    return { finish: finish };

    /* ------------------------------------------------------------- render */

    function speaking() { return pref("mode", "type") === "speak"; }
    function italian()  { return pref("lang", "en") === "it"; }

    function render() {
      var r = queue[idx];
      elCount.textContent = (idx + 1) + " / " + queue.length;
      elBar.style.width = Math.round((idx / queue.length) * 100) + "%";

      var prompt = r.kind === "complete"
        ? md(r.stem) + " <span class=\"viva-gap\">…</span>"
        : "&ldquo;" + md(r.claim) + (r.continues ? " &hellip;" : "") + "&rdquo;";

      var task = r.kind === "complete"
        ? (italian() ? "Completa la regola, poi spiega perché è vera."
                     : "Finish the rule, then say why it is true.")
        : (italian() ? "Perché è vera? Due frasi, come le diresti a un colloquio."
                     : "Why is that true? Two sentences, the way you would say it in an interview.");

      var html =
        '<div class="quiz-from">' + esc(r.part) + "</div>" +
        '<p class="quiz-q viva-claim">' + prompt + "</p>" +
        '<p class="viva-task">' + esc(task) + "</p>";

      if (speaking()) {
        html +=
          '<p class="viva-spoken subtle">Speaking mode. Answer out loud, in full sentences — ' +
          "then reveal and mark yourself honestly.</p>";
      } else {
        html +=
          '<textarea class="viva-input" rows="4" spellcheck="false" ' +
          'placeholder="' + (italian() ? "Scrivi la risposta&hellip;" : "Type the answer&hellip;") +
          '"></textarea>' +
          '<p class="viva-hint subtle"><kbd>Ctrl</kbd> + <kbd>Enter</kbd> to check yourself. ' +
          "Rough notes are fine — full sentences are better.</p>";
      }

      html +=
        '<div class="quiz-actions">' +
          '<button type="button" class="btn primary" data-act="reveal"' +
            (speaking() ? "" : " disabled") + ">" +
            (speaking() ? "I have said it — show me" : "Check yourself") + "</button> " +
          '<button type="button" class="btn quiet" data-act="blank">I can&rsquo;t say it</button>' +
        "</div>" +
        '<div class="viva-reveal" hidden></div>';

      elBody.innerHTML = html;
      startedCard = Date.now();
      startClock();
      wireCard(r);
    }

    function wireCard(r) {
      var input = elBody.querySelector(".viva-input");
      var reveal = elBody.querySelector('[data-act="reveal"]');

      if (input) {
        // The gate on the reveal button is the whole mechanism of this page:
        // 15 characters is not a quality bar, it is a commitment. You cannot
        // read the answer and then decide what you would have said.
        input.addEventListener("input", function () {
          reveal.disabled = input.value.trim().length < 15;
        });
        input.addEventListener("keydown", function (e) {
          if ((e.ctrlKey || e.metaKey) && e.key === "Enter" && !reveal.disabled) {
            e.preventDefault();
            show(r, false);
          }
        });
        input.focus();
      }

      reveal.addEventListener("click", function () { show(r, false); });
      elBody.querySelector('[data-act="blank"]').addEventListener("click", function () {
        show(r, true);
      });
    }

    /* ------------------------------------------------------------- reveal */

    function show(r, gaveUp) {
      stopClock();
      var input = elBody.querySelector(".viva-input");
      var mine = input ? input.value.trim() : "";
      seconds[idx] = Math.round((Date.now() - startedCard) / 1000);

      if (input) input.disabled = true;
      var actions = elBody.querySelector(".quiz-actions");
      if (actions) actions.remove();

      var answer = r.kind === "complete"
        ? "<strong>" + md(r.claim) + "</strong>"
        : "<strong>" + md(r.claim) + "</strong> " + md(r.why);

      var html =
        '<div class="viva-answer">' +
          '<div class="box-title">' + (r.kind === "complete" ? "The rule" : "The justification") + "</div>" +
          "<p>" + answer + "</p>" +
          where(r) +
        "</div>";

      if (r.checkpoints && mine) html += checkpoints(r, mine);

      if (gaveUp) {
        html +=
          '<p class="viva-verdict no">Marked <strong>couldn&rsquo;t say it</strong>. ' +
          "It comes back tomorrow — that is the point of the deck, not a punishment.</p>" +
          '<div class="viva-grade"><button type="button" class="btn primary" data-grade="0">' +
          (idx === queue.length - 1 ? "See results" : "Next rule") + "</button></div>";
      } else {
        html +=
          '<p class="viva-verdict subtle">' +
          (seconds[idx] ? "You took " + seconds[idx] + "s. " : "") +
          "Compare it with what you said, then mark yourself. Generous marking here costs you " +
          "nothing today and everything in the interview.</p>" +
          '<div class="viva-grade">' +
            '<button type="button" class="btn primary" data-grade="2">Said it cleanly</button>' +
            '<button type="button" class="btn" data-grade="1">Roughly — I hedged</button>' +
            '<button type="button" class="btn" data-grade="0">Not really</button>' +
          "</div>";
      }

      var box = elBody.querySelector(".viva-reveal");
      box.hidden = false;
      box.innerHTML = html;

      var buttons = box.querySelectorAll("[data-grade]");
      for (var i = 0; i < buttons.length; i++) {
        buttons[i].addEventListener("click", function () {
          advance(r, parseInt(this.getAttribute("data-grade"), 10));
        });
      }

      // 1 / 2 / 3 grade without reaching for the mouse. Only while a reveal is
      // on screen, and never while something is being typed into.
      graders = box;
      try { box.scrollIntoView({ behavior: "smooth", block: "nearest" }); } catch (e) { /* ignore */ }
    }

    /** Where to go back to. The course is not served over HTTP, so this is a path, not a link. */
    function where(r) {
      if (r.href) {
        return '<p class="viva-where subtle">' + esc(r.part) +
               ' &middot; <code>' + esc(r.href) + "</code></p>";
      }
      if (r.refs && r.refs.length) {
        return '<p class="viva-where subtle">Module ' + esc(r.refs.join(", ")) +
               " in <code>course/</code>.</p>";
      }
      return "";
    }

    /**
     * Which of the rule's own terms the answer actually contains. A literal
     * substring check — it cannot tell whether you used the term correctly, and
     * it says so. Its job is to make "I basically said that" harder to believe
     * when the answer never once mentioned `rowversion`.
     */
    function checkpoints(r, mine) {
      var hay = mine.toLowerCase();
      var chips = r.checkpoints.map(function (term) {
        var hit = hay.indexOf(term.toLowerCase()) !== -1;
        return '<span class="viva-chip ' + (hit ? "hit" : "miss") + '">' +
               (hit ? "✔ " : "✘ ") + esc(term) + "</span>";
      }).join("");

      return '<div class="viva-checks"><div class="box-title">Terms this rule uses</div>' +
        '<div class="viva-chips">' + chips + "</div>" +
        '<p class="subtle">Matched literally against what you wrote — a missed term is not ' +
        "automatically a wrong answer, but it is worth asking why it was not in yours.</p></div>";
    }

    function advance(r, grade) {
      grades[idx] = grade;
      LF.vivaReview(r.id, grade);
      graders = null;

      idx += 1;
      if (idx >= queue.length) finish();
      else render();
    }

    /* -------------------------------------------------------------- clock */

    function startClock() {
      stopClock();
      elClock.textContent = "0:00";
      elClock.classList.remove("urgent");
      ticker = setInterval(function () {
        var s = Math.round((Date.now() - startedCard) / 1000);
        elClock.textContent = Math.floor(s / 60) + ":" + (s % 60 < 10 ? "0" : "") + (s % 60);
        // Not a limit — a mirror. An answer still forming at ninety seconds is
        // an answer that would already have lost the room.
        elClock.classList.toggle("urgent", s >= 90);
      }, 500);
    }

    function stopClock() {
      if (ticker) { clearInterval(ticker); ticker = null; }
    }

    /* ------------------------------------------------------------- finish */

    function finish() {
      stopClock();
      elBar.style.width = "100%";
      elClock.textContent = "";

      var done = grades.filter(function (g) { return g !== undefined; });
      var clean = done.filter(function (g) { return g === 2; }).length;
      var rough = done.filter(function (g) { return g === 1; }).length;
      var lost  = done.filter(function (g) { return g === 0; }).length;
      var total = Math.round(seconds.reduce(function (a, b) { return a + (b || 0); }, 0));

      var verdict;
      if (!done.length)            verdict = "Nothing marked.";
      else if (lost === 0 && rough === 0) verdict = "Every rule, cleanly. That is interview-ready.";
      else if (clean >= done.length * 0.6) verdict = "Solid. The hedged ones are the revision list.";
      else                         verdict = "This is the useful kind of bad session — you now know which sentences you do not actually have.";

      var html =
        '<div class="quiz-result ' + (lost === 0 ? "pass" : "mid") + '">' +
          '<div class="quiz-score">' + clean + " / " + done.length + "</div>" +
          "<p><strong>" + clean + " clean, " + rough + " hedged, " + lost + " not there.</strong> " +
          esc(verdict) + "</p>" +
          '<p class="subtle">' +
          (total ? Math.round(total / Math.max(1, done.length)) + "s per rule on average. " : "") +
          "Everything you missed is scheduled to come back.</p>";

      var missed = queue.filter(function (r, i) { return grades[i] === 0 || grades[i] === 1; });
      if (missed.length) {
        html += '<div class="quiz-wrong"><div class="box-title">Go and re-read these</div><ul>';
        missed.forEach(function (r) {
          var pointer = r.href
            ? "<code>" + esc(r.href) + "</code>"
            : (r.refs && r.refs.length ? "module " + esc(r.refs.join(", ")) : "");
          html += "<li><strong>" + md(r.claim) + "</strong>" +
            (pointer ? ' <span class="subtle">— ' + pointer + "</span>" : "") +
            "</li>";
        });
        html += "</ul></div>";
      }

      html += '<div class="quiz-actions">' +
        '<button type="button" class="btn primary" data-act="again">Another set</button> ' +
        '<a class="btn" href="review.html">Review deck</a> ' +
        '<a class="btn" href="dashboard.html">Dashboard</a></div></div>';

      elBody.innerHTML = html;
      elCount.textContent = clean + " / " + done.length;

      var again = elBody.querySelector('[data-act="again"]');
      if (again) again.addEventListener("click", function () {
        if (typeof o.onAgain === "function") o.onAgain();
      });

      if (typeof o.onFinish === "function") {
        o.onFinish({ clean: clean, rough: rough, lost: lost, total: done.length, seconds: total });
      }
    }
  }

  /* ------------------------------------------------- keyboard, page-level */

  var graders = null;

  document.addEventListener("keydown", function (e) {
    if (!graders || !graders.isConnected) return;
    var t = e.target.tagName;
    if (t === "INPUT" || t === "TEXTAREA" || e.ctrlKey || e.metaKey || e.altKey) return;
    if (e.key !== "1" && e.key !== "2" && e.key !== "3") return;

    // The keys follow the buttons left to right, so 1 is the confident answer.
    var map = { "1": 2, "2": 1, "3": 0 };
    var button = graders.querySelector('[data-grade="' + map[e.key] + '"]');
    if (button) { e.preventDefault(); button.click(); }
  });

  window.LFViva = {
    mount: mount,
    rules: RULES,
    byId: byId,
    modules: modules,
    eighteen: eighteen,
    lapsed: lapsed,
    unseen: unseen,
    today: today,
    md: md,
    pref: pref,
    savePref: savePref,
  };
})();
