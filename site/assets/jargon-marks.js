/* ==========================================================================
   jargon-marks.js — puts the plain-English one-liner next to the word itself.

   WHAT IT DOES
   Walks a chapter's prose, underlines the FIRST appearance of each term in
   jargon.js, and shows that term's one-liner in a small popover when you click
   or tab onto it. Then it appends a "Plain English" box before the pager
   listing everything it marked, which doubles as a revision list and carries
   the switch that turns the underlines off.

   WHY FIRST-APPEARANCE-ONLY
   Because the alternative was tried on paper and it is unreadable: chapter 12
   says "cache" nineteen times, and nineteen dotted underlines in one article
   is not a reading aid, it is a rash. Once per page is enough — you looked it
   up, you carry on — and the end-of-chapter box is where you go back to it.

   WHY THE MARKUP IS GENERATED AND NOT WRITTEN INTO THE CHAPTERS
   Fifty-six chapter files, two hundred-odd terms. Marking them by hand means
   marking them inconsistently, missing the new ones, and rewriting every page
   the day a definition changes. The dictionary is the one place a term is
   spelled out; this file is the one place it is decided how a marked word
   looks and behaves.

   THE THREE RULES THE WALKER OBEYS, each learned by breaking it
     1. Never inside <code>, <pre>, <a>, a heading or an SVG. Marking a word
        inside a code sample changes what the sample says.
     2. Never inside anything this file or another script generated — the quiz,
        the Italian panel, its own box. The walk runs before those exist, and
        the guard is belt and braces for the day the script order changes.
     3. Boundaries are checked by hand, not with \b. Terms here contain "+",
        "-" and spaces ("N+1", "big-O", "OPC UA"), and \b does the wrong thing
        at every one of them.

   Covered in: site/README.md
   ========================================================================== */
(function () {
  "use strict";

  if (!window.JARGON || !window.JARGON.length) return;

  var PREF = "logiflow-jargon";          /* "off" hides the underlines */
  var body = document.body;
  var chapter = body.getAttribute("data-chapter");
  if (!chapter) return;                  /* chapters only, not the tool pages */

  var main = document.querySelector("main.main");
  if (!main) return;

  /* ---------------------------------------------------------------- utils */

  function esc(s) {
    return String(s == null ? "" : s)
      .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function pref() {
    try { return localStorage.getItem(PREF) || "on"; } catch (e) { return "on"; }
  }

  function savePref(v) {
    try { localStorage.setItem(PREF, v); } catch (e) { /* private mode */ }
  }

  /* --------------------------------------------------- the forms to match

     One entry can be written several ways in prose. `also:` carries the
     irregulars; the regular plurals are derived here rather than typed out two
     hundred times, because a list of plurals is a list somebody forgets to
     extend.

     The "O" guard is not hypothetical: "big O" would otherwise also match
     "big Os", which is not a word, and the alternation would have wasted a
     branch on it in every chapter. */

  function formsOf(form) {
    var out = [form];
    var last = form.split(" ").pop();

    /* "big O" must not grow an "Os", and "generics" is already plural. */
    if (last.length <= 2 || /s$/i.test(last)) return out;

    if (/[^aeiou]y$/i.test(form)) out.push(form.slice(0, -1) + "ies");
    else if (/(ch|sh|x|z)$/i.test(form)) out.push(form + "es");
    else out.push(form + "s");

    return out;
  }

  var byForm = {};        /* lower-cased form -> entry */
  var spelling = {};      /* lower-cased form -> the spelling the dictionary uses */
  var strict = {};        /* lower-cased form -> must match that spelling exactly */
  var forms = [];

  window.JARGON.forEach(function (entry) {
    [entry.t].concat(entry.also || []).forEach(function (base) {
      /* An all-capitals term is an acronym, and matching an acronym without
         regard to case is how "CI" marks the Italian "ci", "MES" marks "mes"
         and "REST" marks the English word rest. Those three were all found in
         the first run over the chapters. A term with any lower-case letter in
         it has no such problem and stays case-insensitive, so "Idempotent" at
         the start of a sentence still matches. */
      var exact = !/[a-z]/.test(base);

      formsOf(base).forEach(function (form) {
        var key = form.toLowerCase();
        if (byForm[key]) return;        /* first entry to claim a form keeps it */
        byForm[key] = entry;
        spelling[key] = form;
        strict[key] = exact;
        forms.push(form);
      });
    });
  });

  /* Longest first. JavaScript alternation takes the first branch that matches
     at a position, so with "test" before "unit test" every "unit test" in the
     site would be marked as "test". */
  forms.sort(function (a, b) { return b.length - a.length; });

  var pattern = new RegExp(
    forms.map(function (f) { return f.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"); }).join("|"),
    "gi");

  /* ------------------------------------------------------------- the walk */

  var SKIP = "a, code, pre, kbd, samp, svg, button, h1, h2, h3, h4, h5, h6, " +
             ".crumbs, .box-title, .dissect-title, #toc, #pager, #chapter-quiz, " +
             ".jargon-box, .it-box, .quiz, .jargon";

  function inSkipped(node) {
    var el = node.parentNode;
    while (el && el !== main) {
      if (el.nodeType === 1 && el.matches && el.matches(SKIP)) return true;
      el = el.parentNode;
    }
    return false;
  }

  function boundary(ch) {
    return ch === "" || !/[A-Za-z0-9_-]/.test(ch);
  }

  /* Entries marked on this page, in the order they were met, which is the
     order the chapter introduces them and therefore the order the box lists
     them in. */
  var found = [];
  var taken = {};

  function markNode(node) {
    var text = node.nodeValue;
    if (!text || text.length < 2 || !/[A-Za-z]/.test(text)) return;

    pattern.lastIndex = 0;

    var pieces = null;      /* built only once something actually matches */
    var cut = 0;
    var m;

    while ((m = pattern.exec(text)) !== null) {
      var key = m[0].toLowerCase();
      var entry = byForm[key];
      if (!entry) continue;

      var id = entry.t;
      if (taken[id]) continue;

      var before = m.index === 0 ? "" : text.charAt(m.index - 1);
      var after = text.charAt(m.index + m[0].length);
      if (!boundary(before) || !boundary(after)) continue;

      if (strict[key] && m[0] !== spelling[key]) continue;

      if (!pieces) pieces = [];
      pieces.push(document.createTextNode(text.slice(cut, m.index)));

      var mark = document.createElement("button");
      mark.type = "button";
      mark.className = "jargon";
      mark.setAttribute("data-term", id);
      mark.setAttribute("aria-expanded", "false");
      mark.setAttribute("title", "What does this mean?");
      mark.appendChild(document.createTextNode(m[0]));
      pieces.push(mark);

      cut = m.index + m[0].length;
      taken[id] = true;
      found.push(entry);
    }

    if (!pieces) return;

    pieces.push(document.createTextNode(text.slice(cut)));

    var frag = document.createDocumentFragment();
    pieces.forEach(function (p) { frag.appendChild(p); });
    node.parentNode.replaceChild(frag, node);
  }

  function walk() {
    var walker = document.createTreeWalker(main, NodeFilter.SHOW_TEXT, null);
    var nodes = [];
    var node;

    /* Collected first, replaced second. Replacing a text node while the walker
       is standing on it is how the walk silently stops half way down the page. */
    while ((node = walker.nextNode())) {
      if (!inSkipped(node)) nodes.push(node);
    }

    nodes.forEach(markNode);
  }

  /* --------------------------------------------------------- the popover */

  var pop = null;
  var open = null;        /* the button the popover currently belongs to */

  function build() {
    pop = document.createElement("div");
    pop.className = "jargon-pop";
    pop.id = "jargonPop";
    pop.setAttribute("role", "dialog");
    pop.setAttribute("aria-label", "In plain English");
    pop.setAttribute("tabindex", "-1");
    pop.hidden = true;
    document.body.appendChild(pop);

    pop.addEventListener("keydown", function (e) {
      if (e.key === "Escape") { e.stopPropagation(); close(true); }
    });

    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape" && open) close(true);
    });

    /* Pointer-down rather than click: a click that starts inside the popover
       and ends outside it (selecting the definition to copy it) must not be
       read as "clicked away". */
    document.addEventListener("pointerdown", function (e) {
      if (!open) return;
      if (pop.contains(e.target)) return;
      if (e.target.closest && e.target.closest(".jargon")) return;
      close(false);
    });

    window.addEventListener("resize", function () { if (open) place(open); });
  }

  function chapterHref(n) {
    if (!n || !window.CHAPTERS) return null;
    for (var i = 0; i < window.CHAPTERS.length; i++) {
      if (window.CHAPTERS[i].n === n) {
        return { href: window.CHAPTERS[i].id + ".html", label: n + " · " + window.CHAPTERS[i].title };
      }
    }
    return null;
  }

  function show(button) {
    var entry = lookup(button.getAttribute("data-term"));
    if (!entry) return;

    if (open === button) { close(true); return; }
    if (open) close(false);

    var where = chapterHref(entry.where);

    pop.innerHTML =
      '<p class="jargon-pop-term">' + esc(entry.t) + "</p>" +
      '<p class="jargon-pop-plain">' + esc(entry.plain) + "</p>" +
      (entry.note ? '<p class="jargon-pop-note">' + esc(entry.note) + "</p>" : "") +
      (where && where.href !== location.href.split("/").pop()
        ? '<p class="jargon-pop-where"><a href="' + esc(where.href) + '">The long version: ' +
          esc(where.label) + " &rarr;</a></p>"
        : "") +
      '<button type="button" class="jargon-pop-close" aria-label="Close">&times;</button>';

    pop.querySelector(".jargon-pop-close").addEventListener("click", function () { close(true); });

    pop.hidden = false;
    open = button;
    button.setAttribute("aria-expanded", "true");
    button.setAttribute("aria-controls", "jargonPop");
    place(button);
    pop.focus();
  }

  function lookup(term) {
    for (var i = 0; i < window.JARGON.length; i++) {
      if (window.JARGON[i].t === term) return window.JARGON[i];
    }
    return null;
  }

  /* Document coordinates, not viewport ones: the popover is position:absolute
     inside <body>, so it scrolls with the word it belongs to instead of
     hanging in the air over a paragraph three screens further down.

     Below SHEET it is a bottom sheet instead, because a 320px popover anchored
     to a word on a 360px phone is a popover with nowhere to go. */
  var SHEET = 560;

  function place(button) {
    pop.classList.remove("as-sheet");

    if (window.matchMedia("(max-width: " + SHEET + "px)").matches) {
      pop.classList.add("as-sheet");
      pop.style.left = "";
      pop.style.top = "";
      return;
    }

    var r = button.getBoundingClientRect();
    var sx = window.pageXOffset, sy = window.pageYOffset;
    var w = pop.offsetWidth, h = pop.offsetHeight;
    var margin = 12;

    var left = r.left + sx;
    var max = sx + document.documentElement.clientWidth - w - margin;
    if (left > max) left = max;
    if (left < sx + margin) left = sx + margin;

    /* Under the word, unless there is no room under the word. */
    var below = r.bottom + sy + 8;
    var above = r.top + sy - h - 8;
    var roomBelow = document.documentElement.clientHeight - r.bottom;

    pop.style.left = Math.round(left) + "px";
    pop.style.top = Math.round(roomBelow < h + 16 && above > sy ? above : below) + "px";
  }

  function close(returnFocus) {
    if (!open) return;
    var button = open;
    open = null;
    pop.hidden = true;
    button.setAttribute("aria-expanded", "false");
    button.removeAttribute("aria-controls");
    if (returnFocus) button.focus();
  }

  /* ------------------------------------------------------------- the box */

  function boxHtml() {
    var on = pref() !== "off";

    return '<div class="box-title">Plain English</div>' +
      "<p class=\"jargon-lede\">Every professional word this chapter uses, in one sentence each. " +
      "In the text above, the first time each one appears it is " +
      "<span class=\"jargon-demo\">underlined like this</span> &mdash; click it and the same " +
      "sentence appears beside the word.</p>" +
      '<dl class="jargon-list">' +
      found.map(function (e) {
        return "<dt>" + esc(e.t) + "</dt><dd>" + esc(e.plain) + "</dd>";
      }).join("") +
      "</dl>" +
      '<p class="jargon-switch">' +
        '<button type="button" class="btn" id="jargonToggle" aria-pressed="' + (on ? "true" : "false") + '">' +
          (on ? "Underlines: on" : "Underlines: off") +
        "</button> " +
        '<span class="subtle">Turning them off keeps this list. The choice is remembered on this device.</span>' +
      "</p>";
  }

  function mountBox() {
    if (!found.length) return;

    var box = document.createElement("div");
    box.className = "box jargon-box";
    box.innerHTML = boxHtml();

    var pager = document.getElementById("pager");
    if (pager) main.insertBefore(box, pager);
    else main.appendChild(box);

    box.addEventListener("click", function (e) {
      var b = e.target.closest("#jargonToggle");
      if (!b) return;
      var nowOn = pref() === "off";
      savePref(nowOn ? "on" : "off");
      apply();
      b.setAttribute("aria-pressed", nowOn ? "true" : "false");
      b.textContent = nowOn ? "Underlines: on" : "Underlines: off";
      if (!nowOn) close(false);
    });
  }

  /* Off has to mean off for the keyboard too. CSS can take the underline away
     and refuse the pointer, but a <button> with no visible affordance still
     sits in the tab order, so somebody who turned the hints off would tab
     through forty invisible controls to reach the pager. */
  function apply() {
    var off = pref() === "off";
    body.setAttribute("data-jargon", off ? "off" : "on");

    var marks = main.querySelectorAll(".jargon");
    for (var i = 0; i < marks.length; i++) {
      if (off) marks[i].setAttribute("tabindex", "-1");
      else marks[i].removeAttribute("tabindex");
    }
  }

  /* --------------------------------------------------------------- start */

  walk();
  build();
  mountBox();
  apply();

  main.addEventListener("click", function (e) {
    var b = e.target.closest(".jargon");
    if (!b || !main.contains(b)) return;
    e.preventDefault();
    show(b);
  });

  window.LFJargon = { terms: found, lookup: lookup };
})();
