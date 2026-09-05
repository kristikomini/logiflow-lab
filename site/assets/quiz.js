/* ==========================================================================
   quiz.js — the retrieval engine.

   One renderer serves three surfaces, because they are the same thing with
   different question sources:

     "test"    the end-of-chapter test, on a chapter page
     "review"  today's due cards, drawn from every chapter (review.html)
     "exam"    a timed mixed paper across the whole course (exam.html)

   The interaction is deliberately three clicks or fewer per question:

       pick an option  ->  feedback appears immediately, always, including
                           when you were right
       say how it felt ->  "I was sure" / "I guessed" on a correct answer;
                           a wrong answer needs no button, it graded itself

   That second click is the only thing the spaced-repetition scheduler cannot
   work out for itself. A right answer you had to dig for is not the same as a
   right answer you knew, and scheduling them identically is why so many
   flashcard apps stop working after a month.

   Feedback is shown on correct answers too. Explaining only failures teaches
   people to guess and check.
   ========================================================================== */
(function () {
  "use strict";

  /* ---------------------------------------------------------------- utils */

  function esc(s) {
    return String(s)
      .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  /** Fisher–Yates. Used for option order and for exam question order. */
  function shuffle(list) {
    var a = list.slice();
    for (var i = a.length - 1; i > 0; i--) {
      var j = Math.floor(Math.random() * (i + 1));
      var t = a[i]; a[i] = a[j]; a[j] = t;
    }
    return a;
  }

  function isMulti(q) { return Array.isArray(q.c); }

  function correctSet(q) {
    return isMulti(q) ? q.c.slice().sort() : [q.c];
  }

  /* ------------------------------------------------------------ the bank */

  /**
   * Every question in the bank, flattened, each with a stable id.
   * The id is `<chapter>#<index>` and MUST stay stable — it is the key the
   * spaced-repetition schedule is stored under. Reordering the questions
   * inside a chapter silently reassigns somebody's review history, so add
   * new questions at the END of a chapter's array.
   */
  function all() {
    var bank = window.QUIZZES || {};
    var out = [];
    var chapters = window.CHAPTERS || [];
    for (var i = 0; i < chapters.length; i++) {
      var id = chapters[i].id;
      var qs = bank[id];
      if (!qs) continue;
      for (var j = 0; j < qs.length; j++) {
        out.push(Object.assign({}, qs[j], {
          id: id + "#" + j,
          chapter: id,
          chapterTitle: chapters[i].title,
          chapterNumber: chapters[i].n,
        }));
      }
    }
    return out;
  }

  function forChapter(chapterId) {
    return all().filter(function (q) { return q.chapter === chapterId; });
  }

  function byId(id) {
    var found = null;
    all().some(function (q) { if (q.id === id) { found = q; return true; } return false; });
    return found;
  }

  /* ------------------------------------------------------------- renderer */

  /**
   * Mount a quiz into an element.
   *
   * @param {Element} host
   * @param {object}  opts
   *   questions   array from all()/forChapter()/byId()
   *   mode        "test" | "review" | "exam"
   *   title       heading text
   *   subtitle    one line under the heading
   *   shuffle     shuffle question order (default: true except for "test")
   *   timeLimit   seconds; only meaningful for "exam"
   *   onFinish    function(result)
   */
  function mount(host, opts) {
    if (!host) return null;
    var o = opts || {};
    var mode = o.mode || "test";
    var questions = (o.questions || []).slice();
    if (o.shuffle !== false && mode !== "test") questions = shuffle(questions);
    if (!questions.length) {
      host.innerHTML = '<div class="box"><div class="box-title">Nothing to answer</div>' +
        "<p>" + esc(o.empty || "There are no questions here yet.") + "</p></div>";
      return null;
    }

    var idx = 0;
    var picked = [];          // per question: array of chosen indices
    var graded = [];          // per question: 0 | 1 | 2
    var answeredAt = [];
    var startedAt = Date.now();
    var order = [];           // shuffled option order per question
    var deadline = o.timeLimit ? Date.now() + o.timeLimit * 1000 : 0;
    var ticker = null;

    host.className = "quiz";
    host.innerHTML =
      '<div class="quiz-head">' +
        '<div><div class="quiz-title"></div><div class="quiz-sub subtle"></div></div>' +
        '<div class="quiz-meta"><span class="quiz-count"></span><span class="quiz-clock" hidden></span></div>' +
      "</div>" +
      '<div class="quiz-bar"><span></span></div>' +
      '<div class="quiz-body"></div>';

    var elTitle = host.querySelector(".quiz-title");
    var elSub   = host.querySelector(".quiz-sub");
    var elCount = host.querySelector(".quiz-count");
    var elClock = host.querySelector(".quiz-clock");
    var elBar   = host.querySelector(".quiz-bar span");
    var elBody  = host.querySelector(".quiz-body");

    elTitle.textContent = o.title || "Check yourself";
    elSub.textContent = o.subtitle || "";

    if (deadline) {
      elClock.hidden = false;
      ticker = setInterval(function () {
        var left = Math.max(0, Math.round((deadline - Date.now()) / 1000));
        var m = Math.floor(left / 60), s = left % 60;
        elClock.textContent = m + ":" + (s < 10 ? "0" : "") + s;
        elClock.classList.toggle("urgent", left <= 60);
        if (left <= 0) { clearInterval(ticker); ticker = null; finish(true); }
      }, 250);
    }

    render();
    return { finish: function () { finish(false); } };

    /* ------------------------------------------------------------- render */

    function render() {
      var q = questions[idx];
      elCount.textContent = (idx + 1) + " / " + questions.length;
      elBar.style.width = Math.round((idx / questions.length) * 100) + "%";

      if (!order[idx]) {
        // Shuffle the options but remember where each one came from, so `c`
        // still refers to the authored index.
        var ix = q.a.map(function (_, i) { return i; });
        order[idx] = o.shuffle === false ? ix : shuffle(ix);
      }

      var multi = isMulti(q);
      var html = "";

      if (mode !== "test") {
        html += '<div class="quiz-from">Chapter ' + esc(q.chapterNumber) + " &middot; " +
                esc(q.chapterTitle) + "</div>";
      }
      html += '<p class="quiz-q">' + esc(q.q) + "</p>";
      if (q.code) html += "<pre><code>" + esc(q.code) + "</code></pre>";
      if (multi) html += '<p class="quiz-hint subtle">Select every answer that applies.</p>';

      html += '<div class="quiz-options" role="group">';
      order[idx].forEach(function (original, position) {
        html += '<button type="button" class="quiz-option" data-i="' + original + '">' +
          '<span class="quiz-key">' + "ABCDE".charAt(position) + "</span>" +
          "<span>" + esc(q.a[original]) + "</span></button>";
      });
      html += "</div>";

      if (multi) {
        html += '<div class="quiz-actions"><button type="button" class="btn primary" data-act="submit">Submit answer</button></div>';
      }
      html += '<div class="quiz-feedback" hidden></div>';

      elBody.innerHTML = html;
      picked[idx] = picked[idx] || [];
      wire(q, multi);
    }

    function wire(q, multi) {
      var buttons = elBody.querySelectorAll(".quiz-option");

      for (var i = 0; i < buttons.length; i++) {
        buttons[i].addEventListener("click", function () {
          if (elBody.getAttribute("data-answered")) return;
          var v = parseInt(this.getAttribute("data-i"), 10);
          if (multi) {
            var at = picked[idx].indexOf(v);
            if (at === -1) picked[idx].push(v); else picked[idx].splice(at, 1);
            this.classList.toggle("chosen", at === -1);
          } else {
            picked[idx] = [v];
            reveal(q);
          }
        });
      }

      var submit = elBody.querySelector('[data-act="submit"]');
      if (submit) {
        submit.addEventListener("click", function () {
          if (!picked[idx].length) return;
          reveal(q);
        });
      }
    }

    /* ------------------------------------------------------------- reveal */

    function reveal(q) {
      elBody.setAttribute("data-answered", "1");
      answeredAt[idx] = Date.now();

      var right = correctSet(q);
      var chose = picked[idx].slice().sort();
      var ok = right.length === chose.length && right.every(function (v, i) { return v === chose[i]; });

      var buttons = elBody.querySelectorAll(".quiz-option");
      for (var i = 0; i < buttons.length; i++) {
        var v = parseInt(buttons[i].getAttribute("data-i"), 10);
        buttons[i].disabled = true;
        if (right.indexOf(v) !== -1) buttons[i].classList.add("right");
        else if (chose.indexOf(v) !== -1) buttons[i].classList.add("wrong");
      }
      var submit = elBody.querySelector('[data-act="submit"]');
      if (submit) submit.remove();

      var fb = elBody.querySelector(".quiz-feedback");
      fb.hidden = false;
      fb.className = "quiz-feedback " + (ok ? "ok" : "no");

      var last = idx === questions.length - 1;
      var nextLabel = last ? "See results" : "Next question";

      fb.innerHTML =
        '<div class="quiz-verdict">' + (ok ? "✔ Correct" : "✘ Not quite") + "</div>" +
        "<p>" + esc(q.why) + "</p>" +
        (q.chapter && mode !== "test"
          ? '<p class="subtle"><a href="' + chapterHref(q.chapter) + '">Re-read chapter ' +
            esc(q.chapterNumber) + " &middot; " + esc(q.chapterTitle) + "</a></p>"
          : "") +
        '<div class="quiz-grade">' +
          (ok
            ? '<button type="button" class="btn primary" data-grade="2">I was sure</button>' +
              '<button type="button" class="btn" data-grade="1">I half-guessed</button>'
            : '<button type="button" class="btn primary" data-grade="0">' + nextLabel + "</button>") +
        "</div>";

      var gradeButtons = fb.querySelectorAll("[data-grade]");
      for (var g = 0; g < gradeButtons.length; g++) {
        gradeButtons[g].addEventListener("click", function () {
          advance(q, parseInt(this.getAttribute("data-grade"), 10));
        });
      }

      // Scroll the feedback into view on a small screen, where it is below the fold.
      if (window.innerWidth <= 700) {
        try { fb.scrollIntoView({ behavior: "smooth", block: "nearest" }); } catch (e) { /* ignore */ }
      }
    }

    function advance(q, grade) {
      graded[idx] = grade;

      // Every answered question becomes a scheduled card, wherever it was
      // answered. That is what makes a chapter test feed the review deck.
      if (window.LF) window.LF.review(q.id, grade);

      elBody.removeAttribute("data-answered");
      idx += 1;
      if (idx >= questions.length) finish(false);
      else render();
    }

    /* ------------------------------------------------------------- finish */

    function finish(timedOut) {
      if (ticker) { clearInterval(ticker); ticker = null; }
      var answered = graded.filter(function (g) { return g !== undefined; }).length;
      var correct = graded.filter(function (g) { return g > 0; }).length;
      var total = mode === "exam" && timedOut ? questions.length : (answered || questions.length);
      var seconds = Math.round((Date.now() - startedAt) / 1000);

      elBar.style.width = "100%";
      elCount.textContent = correct + " / " + total;
      elClock.hidden = true;

      var result = {
        mode: mode, correct: correct, total: total, seconds: seconds,
        score: total ? correct / total : 0,
        wrong: questions.filter(function (q, i) { return graded[i] === 0; }),
        timedOut: !!timedOut,
      };

      var recorded = null;
      if (mode === "test" && o.chapter && window.LF) {
        recorded = window.LF.recordTest(o.chapter, correct, total, seconds);
      }
      if (mode === "exam" && window.LF) {
        var p = window.LF.profile;
        if (!p.examBest || result.score > p.examBest) p.examBest = result.score;
        p.examCount = (p.examCount || 0) + 1;
        window.LF.award(2 * correct, "Mock exam");
        window.LF.save(true);
      }

      renderResults(result, recorded);
      if (typeof o.onFinish === "function") o.onFinish(result);
    }

    function renderResults(result, recorded) {
      var pct = Math.round(result.score * 100);
      var verdict, cls;
      if (pct >= 95)      { verdict = "Excellent. This chapter is interview-ready."; cls = "ace"; }
      else if (pct >= 80) { verdict = "Passed. Solid enough to talk about under pressure."; cls = "pass"; }
      else if (pct >= 50) { verdict = "Halfway. Re-read the sections behind the wrong answers, then try again."; cls = "mid"; }
      else                { verdict = "Not yet. This is useful information, not a failure — the wrong ones are now in your review deck."; cls = "low"; }

      var html =
        '<div class="quiz-result ' + cls + '">' +
          '<div class="quiz-score">' + pct + "%</div>" +
          "<p><strong>" + result.correct + " of " + result.total + " correct" +
            (result.timedOut ? " — time ran out" : "") + ".</strong> " + esc(verdict) + "</p>";

      if (recorded && recorded.xp) html += '<p class="quiz-xp">+' + recorded.xp + " XP</p>";
      if (recorded && recorded.improved && recorded.score < 1) {
        html += '<p class="subtle">Your best score for this chapter is now ' +
                Math.round(recorded.score * 100) + "%.</p>";
      }

      if (result.wrong.length) {
        html += '<div class="quiz-wrong"><div class="box-title">Worth going back to</div><ul>';
        result.wrong.forEach(function (q) {
          html += '<li><a href="' + chapterHref(q.chapter) + '">' +
            esc(q.chapterNumber) + " &middot; " + esc(q.chapterTitle) + "</a> — " +
            esc(q.q.length > 90 ? q.q.slice(0, 88) + "…" : q.q) + "</li>";
        });
        html += "</ul><p class=\"subtle\">These are scheduled for review. They will come back tomorrow, " +
                "then at growing intervals until you stop getting them wrong.</p></div>";
      }

      html += '<div class="quiz-actions">' +
        '<button type="button" class="btn primary" data-act="again">Try again</button> ' +
        '<a class="btn" href="' + rel("review.html") + '">Go to review</a> ' +
        '<a class="btn" href="' + rel("dashboard.html") + '">Dashboard</a>' +
        "</div></div>";

      elBody.innerHTML = html;

      var again = elBody.querySelector('[data-act="again"]');
      if (again) {
        again.addEventListener("click", function () {
          idx = 0; picked = []; graded = []; order = []; answeredAt = [];
          startedAt = Date.now();
          questions = shuffle(questions);
          if (o.timeLimit) {
            deadline = Date.now() + o.timeLimit * 1000;
            elClock.hidden = false;
            ticker = setInterval(function () {
              var left = Math.max(0, Math.round((deadline - Date.now()) / 1000));
              var m = Math.floor(left / 60), s = left % 60;
              elClock.textContent = m + ":" + (s < 10 ? "0" : "") + s;
              if (left <= 0) { clearInterval(ticker); ticker = null; finish(true); }
            }, 250);
          }
          render();
        });
      }
    }
  }

  /* ------------------------------------------------------------ link help */

  /** Chapter pages sit one directory down; everything else is at the root. */
  function atRoot() {
    return !document.body.getAttribute("data-chapter");
  }
  function rel(file) { return (atRoot() ? "" : "../") + file; }
  function chapterHref(id) {
    return (atRoot() ? "chapters/" : "") + id + ".html";
  }

  window.LFQuiz = {
    all: all,
    forChapter: forChapter,
    byId: byId,
    shuffle: shuffle,
    mount: mount,
    rel: rel,
    chapterHref: chapterHref,
    count: function () { return all().length; },
  };
})();
