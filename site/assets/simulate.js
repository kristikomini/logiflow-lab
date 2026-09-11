/* ==========================================================================
   simulate.js — the interview simulator.

   WHAT MAKES THIS DIFFERENT FROM EVERY OTHER PAGE HERE
   ====================================================
   The site already has four retrieval surfaces, and between them they cover
   almost everything except the conditions. viva.html asks you to produce an
   answer — but at your own pace, one card at a time, stopping whenever you
   like. review.html and exam.html give you options. None of them reproduce the
   three things that actually make an interview hard:

     1. A CLOCK. Not a generous one. You have about ninety seconds to say
        something coherent, and the sentence you would have produced in four
        minutes is not the sentence you will produce.

     2. NO STOPPING. In a real round you cannot pause after a bad answer to go
        and read the chapter. The next question arrives while you are still
        annoyed about the last one, and recovering from that is a skill.

     3. THE NON-TECHNICAL PARTS. "Mi parli di lei" and "ha domande per noi?"
        are asked in every interview in Modena and are the two people prepare
        least, because they do not feel like knowledge. They are scored anyway.

   So this page runs a continuous, timed, mixed round: an opening, a technical
   middle drawn from both the Golden rules and the question bank, and a close.
   It does not let you go back, and it does not show a model answer until the
   item is over.

   WHAT IT DELIBERATELY DOES NOT DO
   It does not score you. A number out of ten from a self-marked mock is a
   number about your generosity. What it produces instead is a transcript: what
   you actually said, next to what the course says, with the time you took —
   which is reviewable and cannot be rounded into a comforting percentage.

   Grades still feed the same SM-2 schedules as everything else (LF.vivaReview
   and LF.review), so a fumbled answer here comes back in a few days.
   ========================================================================== */
(function () {
  "use strict";

  if (!window.LF) return;

  var LF = window.LF;
  var RULES = window.RULES || [];
  var QUIZZES = window.QUIZZES || {};
  var CHAPTERS = window.CHAPTERS || [];

  var host = document.getElementById("sim");
  if (!host) return;

  /* ------------------------------------------------------------------ utils */

  function esc(s) {
    return String(s == null ? "" : s)
      .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  /* Same inline-markdown subset as viva.js. Escaped first, so nothing in the
     course's markdown can inject markup here. */
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

  function chapterTitle(id) {
    for (var i = 0; i < CHAPTERS.length; i++) {
      if (CHAPTERS[i].id === id) return CHAPTERS[i].n + " · " + CHAPTERS[i].title;
    }
    return id;
  }

  /* ==========================================================================
     The question pool
     ========================================================================== */

  /* The human parts. Written in Italian because that is the language they are
     asked in, with the English gloss underneath — the technical vocabulary
     stays English either way, which is exactly how these conversations go in
     Modena and Bologna.

     There is no "model answer" for these, because there is not one. What each
     carries instead is the thing the question is actually testing, which is
     the part candidates miss: "mi parli di lei" is not asking for your
     biography. */
  var HUMAN = {
    opening: [
      {
        q: "Mi parli un po' di lei.",
        gloss: "Tell me a bit about yourself.",
        tests:
          "Not your biography. Two minutes, structured: what you can do now, one piece of " +
          "evidence for it, and why this job. The failure mode is chronological — starting at " +
          "school and running out of time before the relevant part.",
      },
      {
        q: "Perché ha scelto di candidarsi da noi?",
        gloss: "Why did you apply to us?",
        tests:
          "Whether you read anything about them. Name the domain, the stack, or a product. " +
          "\"Because I want to grow\" is about you and answers nothing.",
      },
      {
        q: "Mi racconti un progetto di cui va fiero e il suo ruolo dentro.",
        gloss: "Tell me about a project you are proud of, and your role in it.",
        tests:
          "Whether you can scope a story. Situation, what YOU did, what happened, what you " +
          "would change. Saying \"we\" throughout is the classic way to describe a project " +
          "without ever describing yourself.",
      },
    ],
    closing: [
      {
        q: "Ha domande per noi?",
        gloss: "Do you have questions for us?",
        tests:
          "The most under-prepared question in the interview, and the only one where you " +
          "choose the subject. Ask about the work: who reviews code, how a change reaches " +
          "production, what the team is struggling with. \"No, tutto chiaro\" reads as " +
          "indifference even when it is nerves.",
      },
      {
        q: "Quali sono le sue aspettative economiche?",
        gloss: "What are your salary expectations?",
        tests:
          "Give a RANGE with a reason (market, RAL for the level, your situation), not a " +
          "single number and not \"whatever you offer\". Module 17 has the Emilia-Romagna " +
          "figures; going in without one is how people find out afterwards they were 4k low.",
      },
    ],
  };

  /* A viva item: produce the justification for a Golden rule. */
  function vivaItems() {
    return RULES.map(function (r) {
      return {
        kind: "viva",
        id: r.id,
        prompt: r.claim,
        answer: r.why,
        checkpoints: r.checkpoints || [],
        source: r.part || ("Module " + r.module),
        seconds: 90,
      };
    });
  }

  /* A bank item, asked WITHOUT its options.
     This is the trick that makes 536 multiple-choice questions usable here:
     the stem is already a real question, and hiding the four answers turns a
     recognition item into a recall one. The options come back afterwards, so
     the comparison is still concrete. */
  function bankItems() {
    var out = [];
    Object.keys(QUIZZES).forEach(function (chapterId) {
      (QUIZZES[chapterId] || []).forEach(function (q, index) {
        if (!q || !q.a || !q.a.length) return;

        var correct = Array.isArray(q.c) ? q.c : [q.c];
        out.push({
          kind: "bank",
          id: chapterId + "#" + index,
          prompt: q.q,
          code: q.code || "",
          answer: correct.map(function (i) { return q.a[i]; }).join("  ·  "),
          why: q.why || "",
          checkpoints: [],
          source: chapterTitle(chapterId),
          seconds: 75,
        });
      });
    });
    return out;
  }

  /* ==========================================================================
     Building a round
     ========================================================================== */

  var ROUNDS = {
    short: { label: "Screening — 10 minutes", technical: 6 },
    full: { label: "Primo colloquio tecnico — 20 minutes", technical: 12 },
    long: { label: "Giornata intera — 35 minutes", technical: 20 },
  };

  function buildRound(size, weakFirst) {
    var viva = vivaItems();
    var bank = bankItems();

    /* Weighted towards what you are worst at, when asked. LF.weakest returns
       chapter ids in ascending mastery, so anything from the bottom third is
       twice as likely to appear — which is what a revision session should do
       and is the opposite of what feels good. */
    if (weakFirst) {
      var weak = {};
      (LF.weakest(13) || []).forEach(function (w) { weak[w.id || w] = true; });
      bank = bank.filter(function (b) { return weak[b.id.split("#")[0]]; }).concat(bank);
    }

    var picked = [];
    var v = shuffle(viva);
    var b = shuffle(bank);
    var count = ROUNDS[size].technical;

    /* Alternate the two kinds. Interleaving is the same principle the review
       deck uses: a session that stays on one topic feels smoother and teaches
       less. */
    for (var i = 0; i < count; i++) {
      var source = i % 2 === 0 ? v : b;
      var item = source.shift();
      if (item) picked.push(item);
    }

    var opener = shuffle(HUMAN.opening)[0];
    var closer = shuffle(HUMAN.closing)[0];

    return [humanItem(opener, "Apertura", 120)]
      .concat(picked)
      .concat([humanItem(closer, "Chiusura", 90)]);
  }

  function humanItem(h, phase, seconds) {
    return {
      kind: "human",
      id: "human:" + h.q,
      prompt: h.q,
      gloss: h.gloss,
      answer: h.tests,
      checkpoints: [],
      source: phase,
      seconds: seconds,
    };
  }

  /* ==========================================================================
     State
     ========================================================================== */

  var round = null;
  var at = 0;
  var transcript = [];
  var tick = null;
  var remaining = 0;
  var startedAt = 0;
  var spoken = false;

  /* ==========================================================================
     Screens
     ========================================================================== */

  function renderStart() {
    stopTimer();

    var options = Object.keys(ROUNDS).map(function (k) {
      return '<option value="' + k + '">' + esc(ROUNDS[k].label) + "</option>";
    }).join("");

    host.innerHTML =
      '<div class="box">' +
        '<div class="box-title">Set the round up</div>' +
        '<p>Once it starts there is no pausing and no going back, because that is the ' +
          'condition being practised. Give yourself somewhere quiet and actually say the ' +
          'answers out loud &mdash; typing them is easier and less like the thing.</p>' +
        '<div class="sim-setup">' +
          '<label>Length <select id="simSize">' + options + "</select></label>" +
          '<label><input type="checkbox" id="simWeak" checked> Weight towards my weakest chapters</label>' +
          '<label><input type="checkbox" id="simSpoken"> Spoken &mdash; do not make me type</label>' +
        "</div>" +
        '<p><button class="btn primary" id="simStart">Start the round</button></p>' +
      "</div>" +
      (RULES.length ? "" :
        '<div class="box trap"><div class="box-title">⚠ The viva deck is missing</div>' +
        "<p>rules.js did not load, so this round will be bank questions only. " +
        "Regenerate it with <code>dotnet run tools/viva-deck.cs</code>.</p></div>");

    document.getElementById("simStart").addEventListener("click", function () {
      spoken = document.getElementById("simSpoken").checked;
      round = buildRound(
        document.getElementById("simSize").value,
        document.getElementById("simWeak").checked
      );
      at = 0;
      transcript = [];
      renderItem();
    });
  }

  function renderItem() {
    var item = round[at];
    if (!item) return renderReport();

    var phase = item.kind === "human" ? item.source : "Tecnica";

    host.innerHTML =
      '<div class="sim-head">' +
        '<span class="sim-phase">' + esc(phase) + "</span>" +
        '<span class="sim-progress">' + (at + 1) + " / " + round.length + "</span>" +
        '<span class="sim-clock" id="simClock">--:--</span>' +
      "</div>" +
      '<div class="box sim-question">' +
        '<div class="box-title">' + (item.kind === "human" ? "🗣" : "🎓") + " " + esc(item.source) + "</div>" +
        "<p class=\"sim-prompt\">" + md(item.prompt) + "</p>" +
        (item.gloss ? '<p class="sim-gloss">' + esc(item.gloss) + "</p>" : "") +
        (item.code ? "<pre><code>" + esc(item.code) + "</code></pre>" : "") +
      "</div>" +
      (spoken
        ? '<div class="box note"><p>Say it out loud, then press <kbd>Enter</kbd>. ' +
          "Nothing is recorded and nothing is compared for you &mdash; that is the harder, " +
          "more honest version.</p></div>"
        : '<textarea id="simAnswer" class="sim-answer" rows="6" ' +
          'placeholder="Answer as you would say it. Sentences, not bullet points."></textarea>') +
      '<p><button class="btn primary" id="simCommit">' +
        (spoken ? "I have said it" : "Commit the answer") +
      "</button> " +
      '<button class="btn" id="simBlank">I could not say it</button></p>';

    var answerBox = document.getElementById("simAnswer");
    if (answerBox) answerBox.focus();

    document.getElementById("simCommit").addEventListener("click", function () { commit(false); });
    document.getElementById("simBlank").addEventListener("click", function () { commit(true); });

    startTimer(item.seconds);
    startedAt = Date.now();
  }

  function commit(blank) {
    var item = round[at];
    var box = document.getElementById("simAnswer");
    var said = blank ? "" : (box ? box.value.trim() : "(spoken)");
    var took = Math.round((Date.now() - startedAt) / 1000);

    stopTimer();
    renderReveal(item, said, took, blank);
  }

  function renderReveal(item, said, took, blank) {
    /* The literal term check, same idea as the viva: it cannot tell whether
       you were right, but "you never said rowversion" is a fact, and facts are
       harder to mark generously than feelings. */
    var hits = "";
    if (item.checkpoints.length && said) {
      var lower = said.toLowerCase();
      hits = '<p class="sim-checks">' + item.checkpoints.map(function (c) {
        var got = lower.indexOf(String(c).toLowerCase()) !== -1;
        return '<span class="' + (got ? "hit" : "miss") + '">' +
          (got ? "✓ " : "✗ ") + esc(c) + "</span>";
      }).join(" ") + "</p>";
    }

    host.innerHTML =
      '<div class="sim-head">' +
        '<span class="sim-phase">Compare</span>' +
        '<span class="sim-progress">' + (at + 1) + " / " + round.length + "</span>" +
        '<span class="sim-clock">' + took + "s</span>" +
      "</div>" +
      '<div class="box"><div class="box-title">The question</div><p>' + md(item.prompt) + "</p></div>" +
      '<div class="box kid"><div class="box-title">🧒 What you said</div>' +
        (said ? "<p>" + esc(said).replace(/\n/g, "<br>") + "</p>"
              : "<p><em>" + (blank ? "Nothing — you passed." : "Spoken, not captured.") + "</em></p>") +
        hits +
      "</div>" +
      '<div class="box pro"><div class="box-title">' +
        (item.kind === "human" ? "🎓 What the question is actually testing" : "🎓 The course's answer") +
      "</div><p>" + md(item.answer) + "</p>" +
      (item.why ? "<p>" + md(item.why) + "</p>" : "") +
      "</div>" +
      '<p class="sim-grade">How did that go?<br>' +
        '<button class="btn" data-g="0">0 &mdash; not there</button> ' +
        '<button class="btn" data-g="1">1 &mdash; hedged, got there eventually</button> ' +
        '<button class="btn primary" data-g="2">2 &mdash; clean</button>' +
      "</p>";

    host.querySelector(".sim-grade").addEventListener("click", function (e) {
      var b = e.target.closest("button[data-g]");
      if (!b) return;
      grade(item, said, took, parseInt(b.getAttribute("data-g"), 10));
    });
  }

  function grade(item, said, took, g) {
    transcript.push({
      prompt: item.prompt, said: said, answer: item.answer,
      source: item.source, seconds: took, grade: g, kind: item.kind,
    });

    /* Straight into the same schedules the rest of the site uses. A human item
       has no card — there is nothing to schedule about "ha domande per noi?"
       and pretending otherwise would pollute the deck. */
    try {
      if (item.kind === "viva") LF.vivaReview(item.id, g);
      else if (item.kind === "bank") LF.review(item.id, g);
    } catch (e) { /* a full deck is not worth losing the round over */ }

    at++;
    if (at < round.length) renderItem();
    else renderReport();
  }

  function renderReport() {
    stopTimer();

    var total = transcript.reduce(function (s, t) { return s + t.seconds; }, 0);
    var clean = transcript.filter(function (t) { return t.grade === 2; }).length;
    var missed = transcript.filter(function (t) { return t.grade === 0; });

    var rows = transcript.map(function (t) {
      var mark = t.grade === 2 ? "✓" : t.grade === 1 ? "~" : "✗";
      return '<tr class="g' + t.grade + '"><td>' + mark + "</td>" +
        "<td>" + md(t.prompt) + "</td>" +
        "<td>" + esc(t.source) + "</td>" +
        "<td>" + t.seconds + "s</td></tr>";
    }).join("");

    host.innerHTML =
      '<div class="box"><div class="box-title">Round over</div>' +
        "<p>" + clean + " of " + transcript.length + " clean, " +
        Math.round(total / 60) + " minutes of talking. " +
        "Everything you fumbled is already scheduled to come back.</p>" +
        /* No percentage. See the header comment: a self-marked score is a
           number about your generosity, and printing one invites people to
           optimise it. */
        "<p>The useful part is below, and it is the transcript rather than the tally: " +
        "the questions that took longest are usually the ones you know but have never " +
        "had to <em>say</em>.</p>" +
      "</div>" +
      (missed.length
        ? '<div class="box trap"><div class="box-title">⚠ Go back to these first</div><ul>' +
          missed.map(function (t) { return "<li>" + md(t.prompt) + " <em>(" + esc(t.source) + ")</em></li>"; }).join("") +
          "</ul></div>"
        : '<div class="box"><div class="box-title">Nothing dropped</div>' +
          "<p>Run a longer round, or turn the weakest-chapter weighting on.</p></div>") +
      '<div class="table-wrap"><table class="sim-table"><thead><tr>' +
        "<th></th><th>Question</th><th>From</th><th>Took</th>" +
      "</tr></thead><tbody>" + rows + "</tbody></table></div>" +
      '<p><button class="btn primary" id="simAgain">Another round</button> ' +
      '<button class="btn" id="simCopy">Copy the transcript</button></p>';

    document.getElementById("simAgain").addEventListener("click", renderStart);
    document.getElementById("simCopy").addEventListener("click", function () {
      var text = transcript.map(function (t) {
        return "Q: " + t.prompt + "\nYou (" + t.seconds + "s, " + t.grade + "/2): " +
          (t.said || "—") + "\nCourse: " + t.answer + "\n";
      }).join("\n");

      if (navigator.clipboard) navigator.clipboard.writeText(text);
      this.textContent = "Copied";
    });
  }

  /* ==========================================================================
     The clock
     ========================================================================== */

  function startTimer(seconds) {
    remaining = seconds;
    paint();

    tick = setInterval(function () {
      remaining--;
      paint();

      /* Time up commits whatever is in the box. It does NOT skip the item —
         being cut off mid-sentence and having to look at what you managed is
         the whole point, and a question that silently vanished would teach
         nothing. */
      if (remaining <= 0) commit(false);
    }, 1000);
  }

  function paint() {
    var el = document.getElementById("simClock");
    if (!el) return;

    var m = Math.floor(Math.max(0, remaining) / 60);
    var s = Math.max(0, remaining) % 60;
    el.textContent = m + ":" + (s < 10 ? "0" : "") + s;
    el.className = "sim-clock" + (remaining <= 15 ? " urgent" : "");
  }

  function stopTimer() {
    if (tick) clearInterval(tick);
    tick = null;
  }

  /* Enter commits, so a spoken round never needs the mouse. */
  document.addEventListener("keydown", function (e) {
    if (e.key !== "Enter" || e.shiftKey) return;
    var commitBtn = document.getElementById("simCommit");
    if (commitBtn && (spoken || e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      commitBtn.click();
    }
  });

  renderStart();
})();
