/* ==========================================================================
   store.js — the learner's profile, and everything that changes about it.

   One global, `window.LF`. Every other script on the site reads and writes
   through it and never touches localStorage directly, because there are three
   places a profile can live and only this file should know that:

     1. memory      — always; the working copy
     2. localStorage— the moment anything changes, so a refresh keeps it
     3. a server    — only when somebody has signed in (see account.js)

   Design notes worth reading before changing anything here:

   * NOTHING here does any I/O that can block. localStorage throws in private
     windows and inside some embedded viewers; every access is wrapped, and a
     failure downgrades to memory-only rather than taking the page down.

   * The profile is a plain JSON object with a version number. Migrations are
     forward-only and live in `migrate()`. Never rename a field without adding
     a migration — somebody has a month of study in the old shape.

   * Scoring is deliberately biased toward RETRIEVAL, not reading. Opening a
     chapter is worth 10 XP; answering its questions is worth ten times that.
     That is not arbitrary: the strongest finding in the learning-science
     literature is that being tested on material beats re-reading it, and a
     progress bar that fills up just by scrolling teaches you to scroll.
   ========================================================================== */
(function () {
  "use strict";

  var VERSION   = 1;
  var KEY       = "logiflow.profile.v1";
  var GUEST     = "guest";

  /* ======================================================================
     1. Storage — never allowed to throw
     ====================================================================== */

  var memoryOnly = false;

  function readRaw() {
    try {
      return window.localStorage.getItem(KEY);
    } catch (e) {
      memoryOnly = true;
      return null;
    }
  }

  function writeRaw(value) {
    if (memoryOnly) return;
    try {
      window.localStorage.setItem(KEY, value);
    } catch (e) {
      // Quota exceeded or a locked-down context. Keep working in memory.
      memoryOnly = true;
    }
  }

  /* ======================================================================
     2. Dates — a "study day" is local, not UTC
     ====================================================================== */

  /** Local calendar day as YYYY-MM-DD. Streaks must follow the learner's
      midnight, not Greenwich's, or a 23:00 session in Italy counts as tomorrow. */
  function today(date) {
    var d = date || new Date();
    var m = d.getMonth() + 1;
    var day = d.getDate();
    return d.getFullYear() + "-" + (m < 10 ? "0" : "") + m + "-" + (day < 10 ? "0" : "") + day;
  }

  function daysBetween(a, b) {
    // a, b are YYYY-MM-DD. Parsed as UTC midday to dodge DST edges entirely.
    var pa = Date.parse(a + "T12:00:00Z");
    var pb = Date.parse(b + "T12:00:00Z");
    if (isNaN(pa) || isNaN(pb)) return 0;
    return Math.round((pb - pa) / 86400000);
  }

  function addDays(dayString, n) {
    var t = Date.parse(dayString + "T12:00:00Z");
    return today(new Date(t + n * 86400000));
  }

  /* ======================================================================
     3. The empty profile
     ====================================================================== */

  function blank(name) {
    return {
      v: VERSION,
      id: GUEST,
      name: name || "Guest",
      email: "",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      xp: 0,

      /** Per chapter: { read, readAt, best, attempts, lastAt, sections } */
      chapters: {},

      /** Spaced-repetition state, keyed by question id ("07-async-and-errors#3"). */
      cards: {},

      /**
       * The same, for the viva deck — the Golden rules you answer in your own
       * words (viva.html), keyed by rule id ("m07-a-function-around-a-column").
       *
       * A separate map rather than a prefix in `cards`, for one blunt reason:
       * review.html counts `cards` against the size of the question bank and
       * says "N of 464 questions are in your schedule". Mixing 362 rules into
       * that map makes every one of those sentences a lie. The scheduler itself
       * is shared — see `schedule()`.
       */
      viva: {},

      /** Sticky notes. Each: { id, ch, text, color, x, y, pinned, createdAt, updatedAt } */
      notes: [],

      /** Earned badges: { badgeId: isoTimestamp } */
      badges: {},

      /** Quiz attempt log, newest last, capped. */
      attempts: [],

      /** Per-day activity: { "2026-09-04": { xp, cards, answers, minutes } } */
      history: {},

      streak: { count: 0, best: 0, lastDay: "" },

      goals: { dailyXp: 60, dailyCards: 20 },

      /** Set by account.js once signed in. */
      remote: { userId: "", syncedAt: "", dirty: false },
    };
  }

  /* ======================================================================
     4. Load + migrate
     ====================================================================== */

  function migrate(p) {
    if (!p || typeof p !== "object") return blank();
    // Fill in anything a newer version added. Forward-only, never destructive.
    var base = blank(p.name);
    for (var k in base) {
      if (Object.prototype.hasOwnProperty.call(base, k) && !(k in p)) p[k] = base[k];
    }
    if (!p.cards || typeof p.cards !== "object") p.cards = {};
    if (!p.viva || typeof p.viva !== "object") p.viva = {};
    if (!p.streak || typeof p.streak !== "object") p.streak = base.streak;
    if (!p.goals || typeof p.goals !== "object") p.goals = base.goals;
    if (!p.remote || typeof p.remote !== "object") p.remote = base.remote;
    if (!Array.isArray(p.notes)) p.notes = [];
    if (!Array.isArray(p.attempts)) p.attempts = [];
    p.v = VERSION;
    return p;
  }

  var profile;
  try {
    var raw = readRaw();
    profile = raw ? migrate(JSON.parse(raw)) : blank();
  } catch (e) {
    profile = blank();
  }

  /* ======================================================================
     5. Event bus — everything that renders progress listens here
     ====================================================================== */

  var listeners = {};

  function on(event, fn) {
    (listeners[event] || (listeners[event] = [])).push(fn);
    return function off() {
      listeners[event] = (listeners[event] || []).filter(function (f) { return f !== fn; });
    };
  }

  function emit(event, payload) {
    var subs = (listeners[event] || []).concat(listeners["*"] || []);
    for (var i = 0; i < subs.length; i++) {
      try { subs[i](payload, event); } catch (e) { /* one bad listener must not stop the rest */ }
    }
  }

  var saveTimer = null;

  /** Persist. Debounced, because dragging a sticky note fires this per pixel. */
  function save(immediate) {
    profile.updatedAt = new Date().toISOString();
    profile.remote.dirty = true;
    if (saveTimer) { clearTimeout(saveTimer); saveTimer = null; }
    if (immediate) {
      writeRaw(JSON.stringify(profile));
    } else {
      saveTimer = setTimeout(function () {
        saveTimer = null;
        writeRaw(JSON.stringify(profile));
      }, 250);
    }
    emit("change", profile);
  }

  // A tab being closed mid-debounce must not lose the last 250 ms.
  window.addEventListener("beforeunload", function () {
    if (saveTimer) { clearTimeout(saveTimer); writeRaw(JSON.stringify(profile)); }
  });

  /* ======================================================================
     6. Levels — named after the job this repository is aimed at
     ====================================================================== */

  var LEVELS = [
    { at: 0,     name: "Curioso",              short: "Curious" },
    { at: 250,   name: "Stagista",             short: "Intern" },
    { at: 700,   name: "Junior Developer",     short: "Junior" },
    { at: 1500,  name: "Developer",            short: "Dev" },
    { at: 2800,  name: "Mid Developer",        short: "Mid" },
    { at: 4600,  name: "Senior Developer",     short: "Senior" },
    { at: 7000,  name: "Tech Lead",            short: "Lead" },
    { at: 10000, name: "Architetto",           short: "Architect" },
  ];

  function level(xp) {
    var x = typeof xp === "number" ? xp : profile.xp;
    var i = 0;
    for (var k = 0; k < LEVELS.length; k++) if (x >= LEVELS[k].at) i = k;
    var cur = LEVELS[i];
    var next = LEVELS[i + 1] || null;
    var span = next ? next.at - cur.at : 1;
    return {
      index: i,
      name: cur.name,
      short: cur.short,
      floor: cur.at,
      next: next,
      into: x - cur.at,
      span: span,
      fraction: next ? Math.min(1, (x - cur.at) / span) : 1,
    };
  }

  /* ======================================================================
     7. XP, streaks and the daily history
     ====================================================================== */

  /** Anti-grind: flashcard XP is capped per day. Reviewing 400 cards in one
      sitting is not four hundred cards' worth of learning, and a number that
      only goes up teaches you to farm it. */
  var DAILY_CARD_XP_CAP = 240;

  function day(d) {
    var key = d || today();
    if (!profile.history[key]) {
      profile.history[key] = { xp: 0, cards: 0, answers: 0, correct: 0, minutes: 0, viva: 0 };
    }
    return profile.history[key];
  }

  function award(amount, reason, opts) {
    var n = Math.max(0, Math.round(amount || 0));
    if (!n) return 0;

    var d = day();
    if (opts && opts.kind === "card") {
      var room = DAILY_CARD_XP_CAP - (d.cardXp || 0);
      if (room <= 0) n = 0;
      else if (n > room) n = room;
      d.cardXp = (d.cardXp || 0) + n;
    }
    if (!n) return 0;

    profile.xp += n;
    d.xp += n;

    emit("xp", { amount: n, reason: reason || "", total: profile.xp });
    checkBadges();
    return n;
  }

  /** Call once per meaningful action. Returns true when the streak grew today. */
  function touchStreak() {
    var t = today();
    var s = profile.streak;
    if (s.lastDay === t) return false;

    var gap = s.lastDay ? daysBetween(s.lastDay, t) : 999;
    s.count = gap === 1 ? s.count + 1 : 1;
    s.lastDay = t;
    if (s.count > s.best) s.best = s.count;

    award(15, "Daily streak day " + s.count);
    emit("streak", s);
    return true;
  }

  /** True if the streak is still alive as of now (today or yesterday). */
  function streakAlive() {
    if (!profile.streak.lastDay) return false;
    var gap = daysBetween(profile.streak.lastDay, today());
    return gap <= 1;
  }

  /* ======================================================================
     8. Chapters and the whole-course percentage
     ====================================================================== */

  function chapter(id) {
    if (!profile.chapters[id]) {
      profile.chapters[id] = {
        read: false, readAt: "", best: 0, attempts: 0, lastAt: "", seconds: 0,
      };
    }
    return profile.chapters[id];
  }

  function markRead(id) {
    var c = chapter(id);
    if (c.read) return false;
    c.read = true;
    c.readAt = new Date().toISOString();
    award(10, "Read chapter " + id);
    touchStreak();
    save();
    emit("chapter", { id: id, state: c });
    return true;
  }

  /**
   * The headline number: how much of the course this person actually knows.
   *
   * A chapter is worth 1/N of the course. Within a chapter:
   *     25%  for having read it
   *     75%  for the best score on its end-of-chapter test
   *
   * Reading is worth something — you cannot answer a question you have never
   * seen — but three quarters of the credit is only available by being tested,
   * which is the whole point.
   */
  function mastery() {
    var list = window.CHAPTERS || [];
    if (!list.length) return { percent: 0, read: 0, tested: 0, total: 0, points: 0 };

    var points = 0, read = 0, tested = 0;
    for (var i = 0; i < list.length; i++) {
      var c = profile.chapters[list[i].id];
      if (!c) continue;
      var p = 0;
      if (c.read) { p += 0.25; read++; }
      if (c.best > 0) { p += 0.75 * Math.min(1, c.best); if (c.best >= 0.8) tested++; }
      points += p;
    }
    return {
      percent: Math.round((points / list.length) * 1000) / 10,
      points: points,
      read: read,
      tested: tested,
      total: list.length,
    };
  }

  /** Mastery for one part of the course (the sidebar groups), 0..1. */
  function partMastery(partName) {
    var list = (window.CHAPTERS || []).filter(function (c) { return c.part === partName; });
    if (!list.length) return { percent: 0, done: 0, total: 0 };
    var pts = 0, done = 0;
    list.forEach(function (ch) {
      var c = profile.chapters[ch.id];
      if (!c) return;
      var p = (c.read ? 0.25 : 0) + 0.75 * Math.min(1, c.best || 0);
      pts += p;
      if (c.read && c.best >= 0.8) done++;
    });
    return { percent: Math.round((pts / list.length) * 100), done: done, total: list.length };
  }

  /**
   * Records one finished end-of-chapter test.
   * @param {string} id      chapter id
   * @param {number} correct questions answered correctly
   * @param {number} total   questions asked
   * @param {number} seconds wall-clock time spent
   */
  function recordTest(id, correct, total, seconds) {
    if (!total) return { xp: 0, improved: false, score: 0 };

    var score = correct / total;
    var c = chapter(id);
    var improved = score > c.best;
    var firstPass = score >= 0.8 && c.best < 0.8;

    c.attempts += 1;
    c.lastAt = new Date().toISOString();
    c.seconds += Math.max(0, Math.round(seconds || 0));
    if (improved) c.best = score;
    if (!c.read) { c.read = true; c.readAt = c.lastAt; }

    var xp = 0;
    if (firstPass) xp += award(50, "Passed the test for " + id);
    if (score === 1 && !c.perfect) { c.perfect = true; xp += award(25, "Perfect score on " + id); }

    profile.attempts.push({
      ch: id, at: c.lastAt, correct: correct, total: total, s: Math.round(seconds || 0),
    });
    if (profile.attempts.length > 400) profile.attempts.splice(0, profile.attempts.length - 400);

    touchStreak();
    save(true);
    emit("chapter", { id: id, state: c });
    emit("test", { id: id, score: score, correct: correct, total: total });

    return { xp: xp, improved: improved, score: score, passed: score >= 0.8 };
  }

  /* ======================================================================
     9. Spaced repetition — SM-2, lightly modernised
     ====================================================================== */

  /*
     Every quiz question doubles as a flashcard. Answer one anywhere on the
     site and it enters the schedule; get it wrong and it comes back tomorrow,
     get it right repeatedly and the gap grows. Because the schedule is global
     rather than per chapter, a review session naturally MIXES chapters — which
     is interleaving, and interleaving is the reason a review session beats
     re-reading one chapter five times.

     Grades: 0 = wrong, 1 = right but hesitant, 2 = right and certain.
  */

  var MIN_EASE = 1.3;

  function slot(map, id) {
    if (!map[id]) {
      map[id] = {
        ease: 2.5, interval: 0, reps: 0, lapses: 0, due: today(), last: "", grade: -1,
      };
    }
    return map[id];
  }

  function card(id) { return slot(profile.cards, id); }

  /**
   * Move one card along the schedule. Written once and shared by both decks —
   * the quiz questions and the viva rules — because two copies of SM-2 would
   * drift apart, and the one nobody tested would be the one that got it wrong.
   */
  function schedule(k, grade) {
    var t = today();

    if (grade <= 0) {
      // Wrong. Back to the start of the ladder, but keep the ease penalty small
      // so one bad day does not bury a card you actually know.
      k.lapses += 1;
      k.reps = 0;
      k.interval = 1;
      k.ease = Math.max(MIN_EASE, k.ease - 0.2);
    } else {
      k.reps += 1;
      if (k.reps === 1) k.interval = 1;
      else if (k.reps === 2) k.interval = grade === 2 ? 4 : 3;
      else k.interval = Math.round(k.interval * k.ease);

      // A hesitant right answer should not stretch the gap as far as a
      // confident one — that is the whole signal SM-2 gets from the learner.
      if (grade === 1) { k.ease = Math.max(MIN_EASE, k.ease - 0.15); k.interval = Math.max(1, Math.round(k.interval * 0.7)); }
      else k.ease = Math.min(3.2, k.ease + 0.1);
    }

    k.interval = Math.min(k.interval, 180);
    k.due = addDays(t, Math.max(1, k.interval));
    k.last = t;
    k.grade = grade;
    return k;
  }

  function review(id, grade) {
    var k = schedule(card(id), grade);

    var d = day();
    d.cards += 1;
    award(grade > 0 ? 5 : 2, "Card reviewed", { kind: "card" });
    touchStreak();
    save();
    emit("card", { id: id, card: k, grade: grade });
    return k;
  }

  /**
   * One viva rule, graded by the learner themselves after they have said the
   * answer out loud or written it down (viva.html).
   *
   * Worth more XP than a multiple-choice card because producing an explanation
   * from nothing is strictly harder than recognising one of four options — and
   * it still goes through the same daily cap, so the two decks cannot be
   * farmed in parallel.
   */
  function vivaReview(id, grade) {
    var k = schedule(slot(profile.viva, id), grade);

    var d = day();
    d.viva = (d.viva || 0) + 1;
    award(grade > 0 ? 8 : 3, "Rule recited", { kind: "card" });
    touchStreak();
    save();
    emit("viva", { id: id, card: k, grade: grade });
    return k;
  }

  function dueFrom(map, limit) {
    var t = today();
    var out = [];
    for (var id in map) {
      if (!Object.prototype.hasOwnProperty.call(map, id)) continue;
      var k = map[id];
      if (daysBetween(k.due, t) >= 0) out.push({ id: id, card: k });
    }
    out.sort(function (a, b) {
      // Overdue longest first; among equals, the ones you keep failing.
      var od = daysBetween(a.card.due, t) - daysBetween(b.card.due, t);
      if (od !== 0) return -od;
      return b.card.lapses - a.card.lapses;
    });
    return typeof limit === "number" ? out.slice(0, limit) : out;
  }

  /** Quiz cards due on or before today, hardest first, optionally limited. */
  function due(limit) { return dueFrom(profile.cards, limit); }

  /** The same for the viva deck. */
  function vivaDue(limit) { return dueFrom(profile.viva, limit); }

  /**
   * Where the viva deck stands. `total` is passed in rather than read from
   * window.RULES, because rules.js is only loaded by the page that needs it and
   * this file must work on every page.
   */
  function vivaStats(total) {
    var ids = Object.keys(profile.viva);
    var strong = 0, lapsed = 0, clean = 0;
    for (var i = 0; i < ids.length; i++) {
      var k = profile.viva[ids[i]];
      if (k.reps >= 2 && k.interval >= 4) strong++;
      if (k.lapses > 0) lapsed++;
      if (k.grade === 2) clean++;
    }
    return {
      seen: ids.length,
      total: total || 0,
      due: vivaDue().length,
      strong: strong,
      lapsed: lapsed,
      clean: clean,
    };
  }

  /** How well the scheduled material is actually being retained, 0..100. */
  function recall() {
    var ids = Object.keys(profile.cards);
    if (!ids.length) return null;
    var strong = 0;
    for (var i = 0; i < ids.length; i++) {
      var k = profile.cards[ids[i]];
      if (k.reps >= 2 && k.interval >= 4) strong++;
    }
    return Math.round((strong / ids.length) * 100);
  }

  /** The chapters this person is worst at, for the "what to fix" panel. */
  function weakest(n) {
    var byChapter = {};
    for (var id in profile.cards) {
      if (!Object.prototype.hasOwnProperty.call(profile.cards, id)) continue;
      var ch = id.split("#")[0];
      var k = profile.cards[id];
      var b = byChapter[ch] || (byChapter[ch] = { ch: ch, lapses: 0, cards: 0 });
      b.lapses += k.lapses;
      b.cards += 1;
    }
    var list = Object.keys(byChapter).map(function (k) { return byChapter[k]; });
    list = list.filter(function (b) { return b.lapses > 0; });
    list.sort(function (a, b) { return b.lapses - a.lapses; });
    return list.slice(0, n || 5);
  }

  /* ======================================================================
     10. Notes
     ====================================================================== */

  var NOTE_COLORS = ["yellow", "green", "blue", "pink", "purple"];

  function addNote(fields) {
    var n = {
      id: "n" + Date.now().toString(36) + Math.random().toString(36).slice(2, 6),
      ch: (fields && fields.ch) || "",
      chTitle: (fields && fields.chTitle) || "",
      text: (fields && fields.text) || "",
      color: (fields && fields.color) || "yellow",
      x: (fields && typeof fields.x === "number") ? fields.x : 24,
      y: (fields && typeof fields.y === "number") ? fields.y : 24,
      pinned: false,
      quote: (fields && fields.quote) || "",
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };
    profile.notes.push(n);
    save();
    emit("note", { action: "add", note: n });
    return n;
  }

  function updateNote(id, fields) {
    for (var i = 0; i < profile.notes.length; i++) {
      if (profile.notes[i].id !== id) continue;
      var n = profile.notes[i];
      for (var k in fields) {
        if (Object.prototype.hasOwnProperty.call(fields, k)) n[k] = fields[k];
      }
      n.updatedAt = new Date().toISOString();
      save();
      emit("note", { action: "update", note: n });
      return n;
    }
    return null;
  }

  function removeNote(id) {
    var before = profile.notes.length;
    profile.notes = profile.notes.filter(function (n) { return n.id !== id; });
    if (profile.notes.length !== before) {
      save();
      emit("note", { action: "remove", id: id });
      return true;
    }
    return false;
  }

  function notesFor(ch) {
    return profile.notes.filter(function (n) { return n.ch === ch; });
  }

  /* ======================================================================
     11. Badges
     ====================================================================== */

  var BADGES = [
    { id: "first-step",   icon: "👣", name: "First step",        hint: "Read your first chapter.",
      test: function (p) { return Object.keys(p.chapters).length >= 1; } },
    { id: "first-test",   icon: "✅", name: "Tested, not read",  hint: "Pass any end-of-chapter test.",
      test: function (p) { return anyChapter(p, function (c) { return c.best >= 0.8; }); } },
    { id: "perfect",      icon: "🎯", name: "Perfect run",       hint: "Score 100% on a chapter test.",
      test: function (p) { return anyChapter(p, function (c) { return c.best >= 1; }); } },
    { id: "perfect-five", icon: "💎", name: "Five perfect",      hint: "Score 100% on five chapters.",
      test: function (p) { return countChapters(p, function (c) { return c.best >= 1; }) >= 5; } },
    { id: "streak-3",     icon: "🔥", name: "Three in a row",    hint: "Study three days running.",
      test: function (p) { return p.streak.best >= 3; } },
    { id: "streak-7",     icon: "🔥", name: "A full week",       hint: "Study seven days running.",
      test: function (p) { return p.streak.best >= 7; } },
    { id: "streak-30",    icon: "🏆", name: "A month straight",  hint: "Study thirty days running.",
      test: function (p) { return p.streak.best >= 30; } },
    { id: "cards-100",    icon: "📇", name: "Hundred recalls",   hint: "Review 100 cards.",
      test: function (p) { return totalCardReviews(p) >= 100; } },
    { id: "cards-500",    icon: "🧠", name: "Five hundred",      hint: "Review 500 cards.",
      test: function (p) { return totalCardReviews(p) >= 500; } },
    { id: "faced-errors", icon: "🪞", name: "Faced your errors", hint: "Bring every lapsed card back to a 4-day interval.",
      test: function (p) {
        var any = false, all = true;
        for (var id in p.cards) {
          if (!Object.prototype.hasOwnProperty.call(p.cards, id)) continue;
          if (p.cards[id].lapses > 0) { any = true; if (p.cards[id].interval < 4) all = false; }
        }
        return any && all;
      } },
    { id: "half",         icon: "⚡", name: "Halfway",           hint: "Reach 50% course mastery.",
      test: function () { return mastery().percent >= 50; } },
    { id: "the-advert",   icon: "📄", name: "Advert covered",    hint: "Pass every chapter the job posting names.",
      test: function (p) { return allOf(p, function (ch) { return !!ch.req; }); } },
    { id: "the-edge",     icon: "🏭", name: "Regional edge",     hint: "Pass every chapter that is NOT in the advert.",
      test: function (p) { return allOf(p, function (ch) { return !!ch.extra; }); } },
    { id: "complete",     icon: "🎓", name: "The whole course",  hint: "Pass all of it.",
      test: function () { return mastery().percent >= 99; } },
    { id: "exam-pass",    icon: "📝", name: "Mock exam passed",  hint: "Score 80% on a mixed exam.",
      test: function (p) { return !!p.examBest && p.examBest >= 0.8; } },
    { id: "exam-ace",     icon: "🎖", name: "Exam ace",          hint: "Score 95% on a mixed exam.",
      test: function (p) { return !!p.examBest && p.examBest >= 0.95; } },
    { id: "note-taker",   icon: "📌", name: "Note taker",        hint: "Write ten sticky notes.",
      test: function (p) { return p.notes.length >= 10; } },
    { id: "viva-first",   icon: "🗣", name: "Said it out loud",  hint: "Answer one Golden rule in your own words.",
      test: function (p) { return Object.keys(p.viva || {}).length >= 1; } },
    { id: "viva-50",      icon: "🎙", name: "Fifty recited",     hint: "Answer 50 viva rules.",
      test: function (p) { return totalVivaReviews(p) >= 50; } },
    { id: "viva-twelve",  icon: "⭐", name: "The twelve, cold",  hint: "Say all twelve interview rules cleanly, without hedging.",
      test: function (p) {
        // Only checkable while the viva deck is loaded, which is exactly when
        // it can become true. Every other page leaves it alone.
        var rules = window.RULES;
        if (!rules) return false;
        var twelve = rules.filter(function (r) { return r.tier === "twelve"; });
        if (!twelve.length) return false;
        return twelve.every(function (r) {
          var k = (p.viva || {})[r.id];
          return k && k.grade === 2;
        });
      } },
    { id: "night-owl",    icon: "🦉", name: "Night shift",       hint: "Answer a question after midnight.",
      test: function () { var h = new Date().getHours(); return h >= 0 && h < 5; } },
  ];

  function anyChapter(p, fn) {
    for (var k in p.chapters) if (Object.prototype.hasOwnProperty.call(p.chapters, k) && fn(p.chapters[k])) return true;
    return false;
  }
  function countChapters(p, fn) {
    var n = 0;
    for (var k in p.chapters) if (Object.prototype.hasOwnProperty.call(p.chapters, k) && fn(p.chapters[k])) n++;
    return n;
  }
  function totalCardReviews(p) {
    var n = 0;
    for (var d in p.history) if (Object.prototype.hasOwnProperty.call(p.history, d)) n += p.history[d].cards || 0;
    return n;
  }
  function totalVivaReviews(p) {
    var n = 0;
    for (var d in p.history) if (Object.prototype.hasOwnProperty.call(p.history, d)) n += p.history[d].viva || 0;
    return n;
  }
  function allOf(p, filter) {
    var list = (window.CHAPTERS || []).filter(filter);
    if (!list.length) return false;
    for (var i = 0; i < list.length; i++) {
      var c = p.chapters[list[i].id];
      if (!c || c.best < 0.8) return false;
    }
    return true;
  }

  var badgeCheckPending = false;

  function checkBadges() {
    // Coalesced: award() calls this, and a single test can call award() twice.
    if (badgeCheckPending) return;
    badgeCheckPending = true;
    setTimeout(function () {
      badgeCheckPending = false;
      var earned = [];
      for (var i = 0; i < BADGES.length; i++) {
        var b = BADGES[i];
        if (profile.badges[b.id]) continue;
        var ok = false;
        try { ok = !!b.test(profile); } catch (e) { ok = false; }
        if (ok) {
          profile.badges[b.id] = new Date().toISOString();
          earned.push(b);
        }
      }
      if (earned.length) {
        save();
        emit("badges", earned);
      }
    }, 0);
  }

  /* ======================================================================
     12. Import / export / reset — a profile you cannot get out is a hostage
     ====================================================================== */

  function exportJson() {
    return JSON.stringify(profile, null, 2);
  }

  function importJson(text) {
    var incoming = JSON.parse(text);
    if (!incoming || typeof incoming !== "object") throw new Error("Not a profile.");
    profile = migrate(incoming);
    save(true);
    emit("replaced", profile);
    return profile;
  }

  function reset() {
    profile = blank();
    save(true);
    emit("replaced", profile);
  }

  /** Replace the in-memory profile (used by account.js after a server pull). */
  function replace(next) {
    profile = migrate(next);
    save(true);
    emit("replaced", profile);
  }

  /* ======================================================================
     13. Public surface
     ====================================================================== */

  window.LF = {
    VERSION: VERSION,
    LEVELS: LEVELS,
    BADGES: BADGES,
    NOTE_COLORS: NOTE_COLORS,

    get profile() { return profile; },
    memoryOnly: function () { return memoryOnly; },

    on: on,
    emit: emit,
    save: save,

    today: today,
    daysBetween: daysBetween,
    addDays: addDays,
    day: day,

    level: level,
    award: award,
    touchStreak: touchStreak,
    streakAlive: streakAlive,

    chapter: chapter,
    markRead: markRead,
    recordTest: recordTest,
    mastery: mastery,
    partMastery: partMastery,

    card: card,
    review: review,
    due: due,
    recall: recall,
    weakest: weakest,

    vivaCard: function (id) { return slot(profile.viva, id); },
    vivaReview: vivaReview,
    vivaDue: vivaDue,
    vivaStats: vivaStats,

    addNote: addNote,
    updateNote: updateNote,
    removeNote: removeNote,
    notesFor: notesFor,

    exportJson: exportJson,
    importJson: importJson,
    reset: reset,
    replace: replace,
  };
})();
