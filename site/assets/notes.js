/* ==========================================================================
   notes.js — sticky notes you can leave on a chapter.

   Four ways to make one:

     * the "+ Note" button in the top bar, which drops a blank note into view
     * selecting text in the chapter, which offers to quote it into a note
     * right-clicking anywhere in the chapter body, which opens a small menu
       and drops the note at the pointer — no selection required
     * pressing N with nothing focused, which is the same as the button

   Notes are absolutely positioned against the DOCUMENT, not the viewport, so
   a note pinned next to the paragraph about deferred execution is still next
   to that paragraph tomorrow. The position is stored as a fraction of the
   main column's width plus an absolute y, so the note stays roughly where it
   was put when the window is a different size — perfect fidelity across
   layouts is not achievable without anchoring to the DOM, and anchoring to
   the DOM breaks the moment a chapter is edited.

   A note can be FOLDED to its title bar, because it sits on top of the
   chapter and a long one covers the paragraph it is about. It can also be
   RESIZED, and it comes to the front when touched — overlapping notes are
   the normal case on a long chapter, not an edge case. Deleting offers an
   undo for seven seconds, and Ctrl+Z goes on working after the bar is gone.

   A note that is scrolled out of view is findable: the rail down the right
   edge marks every off-screen note, in its own colour, at roughly where it
   sits in the page. Clicking a mark scrolls to the note and flashes it.

   Below 980px the drag layer is hidden — dragging small cards around a phone
   screen is nobody's idea of studying — and the same notes are offered as a
   plain editable list in a drawer instead.
   ========================================================================== */
(function () {
  "use strict";

  if (!window.LF) return;

  var body    = document.body;
  var current = body.getAttribute("data-chapter") || "";
  var isChapter = !!current;
  var rel     = function (f) { return (isChapter ? "../" : "") + f; };

  function esc(s) {
    return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;")
      .replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  function toast(msg, kind) {
    if (window.LFLearn && window.LFLearn.toast) window.LFLearn.toast(esc(msg), kind || "", 2200);
  }

  /** Must match .sticky { width } in learn.css — the default, before resizing. */
  var CARD_W = 296;
  var MIN_W = 232, MAX_W = 560, MIN_H = 72, MAX_H = 620;

  /** The width below which the drag layer gives way to the drawer. Must match
      the @media rule in learn.css that hides .note-layer. */
  var NARROW = 980;
  function isNarrow() { return document.documentElement.clientWidth <= NARROW; }

  var layer = null;
  var visible = true;
  try { visible = localStorage.getItem("logiflow.notes.hidden") !== "1"; } catch (e) { /* ignore */ }

  function chapterTitle() {
    var list = window.CHAPTERS || [];
    for (var i = 0; i < list.length; i++) if (list[i].id === current) return list[i].n + " · " + list[i].title;
    return "";
  }

  function mainWidth() {
    var m = document.querySelector(".main");
    return m ? m.clientWidth : 800;
  }

  function mainLeft() {
    var m = document.querySelector(".main");
    if (!m) return 0;
    var r = m.getBoundingClientRect();
    return r.left + window.scrollX;
  }

  function widthOf(n) {
    return Math.max(MIN_W, Math.min(n && n.w ? n.w : CARD_W, MAX_W));
  }

  /* ======================================================================
     The layer
     ====================================================================== */

  function ensureLayer() {
    if (layer) return layer;
    layer = document.createElement("div");
    layer.className = "note-layer";
    layer.hidden = !visible;
    document.body.appendChild(layer);
    return layer;
  }

  /** Highest z in use, so "bring to front" is one number and not a re-sort. */
  var topZ = 0;

  function render() {
    if (!isChapter) return;
    ensureLayer();
    layer.innerHTML = "";
    var notes = window.LF.notesFor(current);

    // Draw in stored stacking order, and renumber from 1 so a long session of
    // clicking does not push z into the thousands.
    notes = notes.slice().sort(function (a, b) { return (a.z || 0) - (b.z || 0); });
    topZ = 0;
    notes.forEach(function (n, i) {
      var card = build(n);
      topZ = i + 1;
      card.style.zIndex = String(topZ);
      if ((n.z || 0) !== topZ) window.LF.updateNote(n.id, { z: topZ });
      layer.appendChild(card);
    });

    layer.hidden = !visible || !notes.length;
    paintButton();
    paintRail();
  }

  function build(n) {
    var card = document.createElement("div");
    card.className = "sticky sticky-" + (n.color || "yellow");
    card.setAttribute("data-id", n.id);

    var w = widthOf(n);
    card.style.width = w + "px";

    // Keep the whole card on screen: a note whose text is off the right edge
    // is worse than one that has drifted a little from where it was dropped.
    var viewport = document.documentElement.clientWidth;
    var left = mainLeft() + (typeof n.x === "number" ? n.x : 0.7) * mainWidth();
    card.style.left = Math.max(8, Math.min(left, viewport - w - 12)) + "px";
    card.style.top  = (n.y || 200) + "px";

    var swatches = window.LF.NOTE_COLORS.map(function (c) {
      return '<button type="button" class="sw sw-' + c + (c === n.color ? " on" : "") +
             '" data-color="' + c + '" title="' + c + '"></button>';
    }).join("");

    if (n.collapsed) card.classList.add("collapsed");

    card.innerHTML =
      '<div class="sticky-bar">' +
        '<span class="sticky-grip" title="Drag">⠿</span>' +
        '<span class="sticky-peek"></span>' +
        '<span class="sticky-saved" aria-hidden="true">saved</span>' +
        '<span class="sticky-colors">' + swatches + "</span>" +
        '<button type="button" class="sticky-fold" title="Collapse (double-click the bar)">' +
          (n.collapsed ? "▸" : "▾") +
        "</button>" +
        '<button type="button" class="sticky-x" title="Delete this note">×</button>' +
      "</div>" +
      (n.quote ? '<blockquote class="sticky-quote">' + esc(n.quote) + "</blockquote>" : "") +
      '<div class="sticky-text" contenteditable="true" spellcheck="false" ' +
      'data-placeholder="Write it in your own words…">' + esc(n.text) + "</div>" +
      '<span class="sticky-resize" title="Resize — double-click for the default size"></span>';

    if (n.h) card.querySelector(".sticky-text").style.height = Math.max(MIN_H, Math.min(n.h, MAX_H)) + "px";

    wireCard(card, n);
    return card;
  }

  /* ---------------------------------------------------------- stacking

     Notes overlap. Whichever one you last touched must be on top, or you end
     up dragging a note out of the way just to read the one beneath it.
     ---------------------------------------------------------------------- */

  function toFront(card, n) {
    if (parseInt(card.style.zIndex, 10) === topZ) return;
    topZ += 1;
    card.style.zIndex = String(topZ);
    window.LF.updateNote(n.id, { z: topZ });
  }

  function wireCard(card, n) {
    var text = card.querySelector(".sticky-text");

    card.addEventListener("pointerdown", function () { toFront(card, n); }, true);

    /* NOT textContent, and NOT innerText either.

       A contenteditable div stores a line break as <div>…</div> or <br>, and
       textContent concatenates those with nothing between them: "line one" and
       "line two" come back as "line oneline two" — every paragraph the reader
       typed, fused into one. innerText does have the newline, but it is defined
       in terms of RENDERED text and quietly falls back to textContent whenever
       the element is not being drawn, which is exactly what the <980px rule
       hiding the whole note layer does. Saving would then keep the breaks on a
       wide window and lose them on a narrow one — the worst kind of bug.

       So walk the nodes instead: deterministic, and independent of layout. */
    function read() {
      var BLOCK = /^(DIV|P|LI|UL|OL|BLOCKQUOTE|H[1-6]|PRE|TR)$/;
      var out = "";
      (function walk(node) {
        for (var c = node.firstChild; c; c = c.nextSibling) {
          if (c.nodeType === 3) { out += c.nodeValue; continue; }
          if (c.nodeType !== 1) continue;
          if (c.nodeName === "BR") { out += "\n"; continue; }
          if (BLOCK.test(c.nodeName) && out && out.slice(-1) !== "\n") out += "\n";
          walk(c);
        }
      })(text);
      return out.replace(/ /g, " ").replace(/\n+$/, "");
    }

    /* A note saves itself 400ms after you stop typing, which is invisible —
       and invisible saving is indistinguishable from no saving at all when the
       thing you are typing into is a card floating over a page. The word in
       the bar fades in for a second when a write actually happened. */
    var savedTag = card.querySelector(".sticky-saved");
    var savedTimer = null;
    function flashSaved() {
      if (!savedTag) return;
      savedTag.classList.add("on");
      if (savedTimer) clearTimeout(savedTimer);
      savedTimer = setTimeout(function () { savedTag.classList.remove("on"); }, 1100);
    }

    var saveTimer = null;
    var lastSaved = n.text;
    function commit() {
      var t = read();
      if (t === lastSaved) return;
      lastSaved = t;
      window.LF.updateNote(n.id, { text: t });
      flashSaved();
      paintRail();
    }

    text.addEventListener("input", function () {
      peek(card, read());
      if (saveTimer) clearTimeout(saveTimer);
      saveTimer = setTimeout(commit, 400);
    });
    text.addEventListener("blur", function () {
      if (saveTimer) clearTimeout(saveTimer);
      commit();
    });

    // Paste as plain text. A paragraph dragged in from the chapter otherwise
    // brings its <code>, its colours and its font size with it, and a sticky
    // full of syntax-highlighted HTML is unreadable.
    text.addEventListener("paste", function (e) {
      var t = (e.clipboardData || window.clipboardData).getData("text/plain");
      e.preventDefault();
      document.execCommand("insertText", false, t);
    });

    text.addEventListener("keydown", function (e) {
      // Escape gets you out of the note without reaching for the mouse.
      if (e.key === "Escape") { e.stopPropagation(); commit(); text.blur(); return; }
      // Ctrl+Enter is "done with this one": save and step away, so the
      // single-key shortcuts are reachable again without using the mouse.
      if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
        e.preventDefault(); commit(); text.blur();
      }
    });

    peek(card, n.text);

    card.querySelector(".sticky-x").addEventListener("click", function () {
      card.classList.add("going");
      var doomed = snapshot(n);
      setTimeout(function () {
        window.LF.removeNote(n.id);
        render();
        offerUndo(doomed);
      }, 160);
    });

    var fold = card.querySelector(".sticky-fold");
    fold.addEventListener("click", function () { toggleFold(card, n); });
    card.querySelector(".sticky-bar").addEventListener("dblclick", function (e) {
      if (e.target.closest(".sw, .sticky-x, .sticky-fold")) return;
      toggleFold(card, n);
    });

    var swatches = card.querySelectorAll(".sw");
    for (var i = 0; i < swatches.length; i++) {
      swatches[i].addEventListener("click", function () {
        var c = this.getAttribute("data-color");
        window.LF.updateNote(n.id, { color: c });
        n.color = c;
        card.className = "sticky sticky-" + c + (card.classList.contains("collapsed") ? " collapsed" : "");
        var all = card.querySelectorAll(".sw");
        for (var k = 0; k < all.length; k++) {
          all[k].classList.toggle("on", all[k].getAttribute("data-color") === c);
        }
        paintRail();
      });
    }

    drag(card, card.querySelector(".sticky-bar"), n);
    resize(card, card.querySelector(".sticky-resize"), n, text);
  }

  /* ---------------------------------------------------------- folding

     A note sits ON TOP of the chapter, so a long one parks itself over the
     paragraph it is about. Folding leaves the bar and the first few words —
     enough to know which note it is — and gives the text back.
     ---------------------------------------------------------------------- */

  function toggleFold(card, n) {
    var now = !card.classList.contains("collapsed");
    card.classList.toggle("collapsed", now);
    card.querySelector(".sticky-fold").textContent = now ? "▸" : "▾";
    n.collapsed = now;
    window.LF.updateNote(n.id, { collapsed: now });
  }

  /** The first line, shown in the bar while the note is folded. */
  function peek(card, txt) {
    var el = card.querySelector(".sticky-peek");
    if (!el) return;
    var first = String(txt || "").split("\n")[0].trim();
    if (first.length > 34) first = first.slice(0, 33) + "…";
    el.textContent = first || "empty note";
  }

  /* ------------------------------------------------------------- undo

     Deleting used to be instant and final: one mis-click on a 14px × and an
     evening of notes was gone, with no confirmation and nothing to press.
     A confirm dialog on every delete is worse — this is the cheap version of
     being careful, and it is the one people actually keep.

     The bar times out after seven seconds; the stack behind it does not, so
     Ctrl+Z still brings back a note deleted a few minutes ago.
     ---------------------------------------------------------------------- */

  var undoBar = null, undoTimer = null;
  var trash = [];

  function snapshot(n) {
    return {
      ch: n.ch, chTitle: n.chTitle, quote: n.quote, text: n.text,
      color: n.color, x: n.x, y: n.y, w: n.w, h: n.h, z: n.z, collapsed: n.collapsed,
    };
  }

  function undoLast() {
    if (!trash.length) return false;
    window.LF.addNote(trash.pop());
    visible = true;
    savePref();
    render();
    hideUndo();
    toast("Note restored");
    return true;
  }

  function offerUndo(note) {
    trash.push(note);
    if (trash.length > 20) trash.shift();

    if (!undoBar) {
      undoBar = document.createElement("div");
      undoBar.className = "note-undo";
      undoBar.innerHTML = '<span class="note-undo-text">Note deleted</span>' +
                          '<button type="button" class="note-undo-btn">Undo</button>' +
                          '<kbd class="note-undo-kbd">Ctrl+Z</kbd>';
      document.body.appendChild(undoBar);
      // One delegated handler, bound once. Binding per delete is how the old
      // version could restore the same note twice.
      undoBar.addEventListener("click", function (e) {
        if (e.target.closest(".note-undo-btn")) undoLast();
      });
    }

    undoBar.hidden = false;
    undoBar.classList.add("in");
    if (undoTimer) clearTimeout(undoTimer);
    undoTimer = setTimeout(hideUndo, 7000);
  }

  function hideUndo() {
    if (!undoBar) return;
    undoBar.classList.remove("in");
    undoBar.hidden = true;
  }

  /* ------------------------------------------------------------- dragging */

  function drag(card, handle, n) {
    var startX = 0, startY = 0, originLeft = 0, originTop = 0, moving = false;

    handle.addEventListener("pointerdown", function (e) {
      if (e.target.closest(".sw") || e.target.closest(".sticky-x") || e.target.closest(".sticky-fold")) return;
      moving = true;
      card.classList.add("dragging");
      startX = e.clientX; startY = e.clientY;
      originLeft = parseFloat(card.style.left) || 0;
      originTop  = parseFloat(card.style.top) || 0;
      handle.setPointerCapture(e.pointerId);
      e.preventDefault();
    });

    handle.addEventListener("pointermove", function (e) {
      if (!moving) return;
      var w = card.offsetWidth || CARD_W;
      var nx = originLeft + (e.clientX - startX);
      var ny = originTop + (e.clientY - startY);
      nx = Math.max(4, Math.min(nx, document.documentElement.clientWidth - w - 12));
      // Below the top bar, and never past the end of the page — a note dragged
      // into the void is unreachable without editing localStorage.
      ny = Math.max(64, Math.min(ny, document.documentElement.scrollHeight - 80));
      card.style.left = nx + "px";
      card.style.top = ny + "px";
    });

    function end(e) {
      if (!moving) return;
      moving = false;
      card.classList.remove("dragging");
      try { handle.releasePointerCapture(e.pointerId); } catch (err) { /* ignore */ }
      n.x = (parseFloat(card.style.left) - mainLeft()) / mainWidth();
      n.y = parseFloat(card.style.top);
      window.LF.updateNote(n.id, { x: n.x, y: n.y });
      paintRail();
    }
    handle.addEventListener("pointerup", end);
    handle.addEventListener("pointercancel", end);
  }

  /* ------------------------------------------------------------- resizing

     One fixed size cannot serve both "→ deferred, not lazy" and a worked
     example copied out of the chapter. The corner grip changes the card's
     width and the writing area's height; both are stored, so the note opens
     tomorrow at the size the thought needed.

     CSS `resize: both` on the text would have been free, but it resizes the
     writing area and leaves the card's width behind, which looks broken.
     ---------------------------------------------------------------------- */

  function resize(card, grip, n, text) {
    if (!grip) return;
    var startX = 0, startY = 0, w0 = 0, h0 = 0, sizing = false;

    grip.addEventListener("pointerdown", function (e) {
      if (card.classList.contains("collapsed")) return;
      sizing = true;
      card.classList.add("sizing");
      startX = e.clientX; startY = e.clientY;
      w0 = card.offsetWidth;
      h0 = text.offsetHeight;
      grip.setPointerCapture(e.pointerId);
      e.preventDefault();
      e.stopPropagation();
    });

    grip.addEventListener("pointermove", function (e) {
      if (!sizing) return;
      var left = parseFloat(card.style.left) || 0;
      var room = document.documentElement.clientWidth - left - 12;
      var w = Math.max(MIN_W, Math.min(w0 + (e.clientX - startX), MAX_W, room));
      var h = Math.max(MIN_H, Math.min(h0 + (e.clientY - startY), MAX_H));
      card.style.width = w + "px";
      text.style.height = h + "px";
    });

    function end(e) {
      if (!sizing) return;
      sizing = false;
      card.classList.remove("sizing");
      try { grip.releasePointerCapture(e.pointerId); } catch (err) { /* ignore */ }
      n.w = card.offsetWidth;
      n.h = text.offsetHeight;
      window.LF.updateNote(n.id, { w: n.w, h: n.h });
    }
    grip.addEventListener("pointerup", end);
    grip.addEventListener("pointercancel", end);

    // Double-clicking the grip gives the default size back, which is the only
    // way out of a note accidentally dragged down to a sliver.
    grip.addEventListener("dblclick", function () {
      card.style.width = CARD_W + "px";
      text.style.height = "";
      n.w = 0; n.h = 0;
      window.LF.updateNote(n.id, { w: 0, h: 0 });
    });
  }

  /* ======================================================================
     The rail — finding a note you cannot see

     Notes live in document coordinates on a chapter that is twenty screens
     long, so the honest question after a week of studying is not "where is
     this note" but "is there one at all". The rail answers both: a mark per
     off-screen note, in its colour, at roughly its place in the page.
     ====================================================================== */

  var rail = null, railRaf = 0;

  function ensureRail() {
    if (rail) return rail;
    rail = document.createElement("div");
    rail.className = "note-rail";
    rail.hidden = true;
    document.body.appendChild(rail);
    rail.addEventListener("click", function (e) {
      var m = e.target.closest(".note-mark");
      if (m) jumpTo(m.getAttribute("data-id"));
    });
    return rail;
  }

  function jumpTo(id) {
    var card = layer && layer.querySelector('[data-id="' + id + '"]');
    if (!card) return;
    var top = parseFloat(card.style.top) || 0;
    window.scrollTo({ top: Math.max(0, top - 120), behavior: "smooth" });
    card.classList.remove("flash");
    void card.offsetWidth;                      // reflow, or the class never restarts
    card.classList.add("flash");
    setTimeout(function () { card.classList.remove("flash"); }, 1300);
  }

  /** One mark per note that is off screen, placed where the note is in the page. */
  function paintRail() {
    if (!isChapter) return;
    ensureRail();
    if (!visible || isNarrow()) { rail.hidden = true; return; }

    var notes = window.LF.notesFor(current);
    if (!notes.length) { rail.hidden = true; return; }

    var vh = document.documentElement.clientHeight;
    var docH = Math.max(document.documentElement.scrollHeight, 1);
    var top = window.scrollY, bottom = top + vh;

    var html = "";
    notes.forEach(function (n) {
      var y = n.y || 0;
      if (y > top - 40 && y < bottom - 20) return;          // already in view
      var pos = Math.max(2, Math.min(96, (y / docH) * 100));
      var first = String(n.text || n.quote || "").split("\n")[0].trim().slice(0, 60);
      html += '<button type="button" class="note-mark note-mark-' + (n.color || "yellow") +
              '" data-id="' + n.id + '" style="top:' + pos.toFixed(2) + '%" title="' +
              esc(first || "empty note") + '"></button>';
    });

    rail.innerHTML = html;
    rail.hidden = !html;
  }

  function railOnScroll() {
    if (railRaf) return;
    railRaf = requestAnimationFrame(function () { railRaf = 0; paintRail(); });
  }

  /* ======================================================================
     Tidying up
     ====================================================================== */

  /** Stack every note for this chapter down the right-hand gutter, folded, in
      page order, starting at the current scroll position. The escape hatch
      for a chapter whose notes have ended up in a heap on one paragraph. */
  function tidy() {
    var notes = window.LF.notesFor(current);
    if (!notes.length) return;
    notes = notes.slice().sort(function (a, b) { return (a.y || 0) - (b.y || 0); });

    var viewport = document.documentElement.clientWidth;
    var y = window.scrollY + 96;
    notes.forEach(function (n) {
      var w = widthOf(n);
      var left = Math.max(8, Math.min(mainLeft() + mainWidth() - w - 8, viewport - w - 12));
      window.LF.updateNote(n.id, {
        x: (left - mainLeft()) / mainWidth(),
        y: y,
        collapsed: true,
      });
      y += 44;
    });
    visible = true;
    savePref();
    render();
    toast(notes.length + " note" + (notes.length === 1 ? "" : "s") + " tidied and folded");
  }

  /* ======================================================================
     Creating notes
     ====================================================================== */

  /**
   * `at` is optional. Given {x, y} in DOCUMENT pixels — what a right-click
   * hands us — the note is dropped there instead of in the default parking
   * spot at the right of the column.
   */
  function create(quote, at) {
    if (!isChapter) { location.href = rel("notes.html"); return; }
    if (isNarrow()) { openDrawer(quote); return; }

    var existing = window.LF.notesFor(current).length;
    var x, y;
    if (at) {
      x = (Math.max(mainLeft(), at.x - CARD_W / 2) - mainLeft()) / mainWidth();
      y = Math.max(window.scrollY + 8, at.y - 18);
    } else {
      x = Math.max(0, (mainWidth() - CARD_W - 16) / mainWidth());
      y = window.scrollY + 120 + (existing % 5) * 26;
    }
    var n = window.LF.addNote({
      ch: current,
      chTitle: chapterTitle(),
      quote: quote || "",
      text: "",
      color: window.LF.NOTE_COLORS[existing % window.LF.NOTE_COLORS.length],
      x: x,
      y: y,
      z: topZ + 1,
    });
    visible = true;
    savePref();
    render();
    var card = layer.querySelector('[data-id="' + n.id + '"]');
    if (card) {
      card.classList.add("fresh");
      var t = card.querySelector(".sticky-text");
      if (t) t.focus();
    }
  }

  /* -------------------------------------------------- select-to-note */

  var bubble = null;

  function selectionBubble() {
    if (!isChapter) return;

    document.addEventListener("mouseup", function (e) {
      if (e.target.closest && (e.target.closest(".sticky") || e.target.closest(".note-bubble"))) return;
      setTimeout(check, 10);
    });
    document.addEventListener("scroll", hide, { passive: true });

    function check() {
      var sel = window.getSelection();
      if (!sel || sel.isCollapsed) { hide(); return; }
      var text = sel.toString().trim();
      if (text.length < 8) { hide(); return; }
      var main = document.querySelector(".main");
      if (main && !main.contains(sel.anchorNode)) { hide(); return; }

      var rect;
      try { rect = sel.getRangeAt(0).getBoundingClientRect(); } catch (err) { return; }
      if (!rect || !rect.width) { hide(); return; }

      if (!bubble) {
        bubble = document.createElement("button");
        bubble.type = "button";
        bubble.className = "note-bubble";
        bubble.textContent = "📌 Note this";
        document.body.appendChild(bubble);
        bubble.addEventListener("click", function () {
          var s = window.getSelection();
          var q = s ? s.toString().trim() : "";
          if (q.length > 240) q = q.slice(0, 238) + "…";
          hide();
          if (s) s.removeAllRanges();
          create(q);
        });
      }
      bubble.style.left = (window.scrollX + rect.left + rect.width / 2) + "px";
      bubble.style.top  = (window.scrollY + rect.top - 8) + "px";
      bubble.hidden = false;
    }

    function hide() { if (bubble) bubble.hidden = true; }
  }

  /* -------------------------------------------------- right-click-to-note

     Right-clicking the chapter body opens our own small menu and drops a
     note exactly where the mouse was. No selection needed — the common case
     is "I want to say something about this paragraph", not "I want to quote
     it". If there IS a selection under the cursor it gets quoted too, so the
     two ways of making a note do not contradict each other.

     The browser menu is only suppressed inside .main, and never on a link, an
     image, an input or an existing sticky — copy-link-address and paste have
     to keep working where a reader expects them.
     ====================================================================== */

  var menu = null;

  function closeMenu() { if (menu) menu.hidden = true; }

  function contextMenu() {
    if (!isChapter) return;

    var main = document.querySelector(".main");
    if (!main) return;

    main.addEventListener("contextmenu", function (e) {
      if (e.shiftKey) return;                    // shift = give me the real menu
      if (e.target.closest(".sticky, .note-bubble, a, img, input, textarea, [contenteditable='true']")) return;

      e.preventDefault();
      open(e.clientX + window.scrollX, e.clientY + window.scrollY, quoteUnder(e));
    });

    document.addEventListener("pointerdown", function (e) {
      if (menu && !menu.hidden && !e.target.closest(".note-menu")) closeMenu();
    });
    document.addEventListener("scroll", closeMenu, { passive: true });
    document.addEventListener("keydown", function (e) { if (e.key === "Escape") closeMenu(); });

    /** The selection, but only if the click landed inside it. */
    function quoteUnder(e) {
      var sel = window.getSelection();
      if (!sel || sel.isCollapsed) return "";
      var q = sel.toString().trim();
      if (q.length < 8) return "";
      var r;
      try { r = sel.getRangeAt(0).getBoundingClientRect(); } catch (err) { return ""; }
      if (!r || e.clientX < r.left - 4 || e.clientX > r.right + 4 ||
          e.clientY < r.top - 4 || e.clientY > r.bottom + 4) return "";
      return q.length > 240 ? q.slice(0, 238) + "…" : q;
    }

    function open(x, y, quote) {
      if (!menu) {
        menu = document.createElement("div");
        menu.className = "note-menu";
        menu.innerHTML =
          '<button type="button" class="note-menu-item" data-act="note">' +
            '📌 <span class="note-menu-label">New note here</span>' +
          "</button>" +
          '<button type="button" class="note-menu-item" data-act="tidy">🧹 Tidy the notes on this page</button>' +
          '<button type="button" class="note-menu-item" data-act="hide">' +
            '🙈 <span class="note-menu-hide">Hide the notes</span>' +
          "</button>" +
          '<button type="button" class="note-menu-item" data-act="all">🗂️ All my notes</button>';
        document.body.appendChild(menu);
        menu.addEventListener("click", function (ev) {
          var b = ev.target.closest(".note-menu-item");
          if (!b) return;
          var act = b.getAttribute("data-act");
          closeMenu();
          if (act === "all")  { location.href = rel("notes.html"); return; }
          if (act === "tidy") { tidy(); return; }
          if (act === "hide") { toggleVisible(); return; }
          var s = window.getSelection();
          if (s && menu.dataset.quote) s.removeAllRanges();
          create(menu.dataset.quote || "", { x: +menu.dataset.x, y: +menu.dataset.y });
        });
      }

      menu.dataset.quote = quote || "";
      menu.dataset.x = x;
      menu.dataset.y = y;
      menu.querySelector(".note-menu-label").textContent = quote ? "Note this quote" : "New note here";

      // Only offer what there is something to do to.
      var count = window.LF.notesFor(current).length;
      menu.querySelector('[data-act="tidy"]').hidden = count < 2 || !visible;
      menu.querySelector('[data-act="hide"]').hidden = !count;
      menu.querySelector(".note-menu-hide").textContent = visible ? "Hide the notes" : "Show the notes";

      // Measure before placing, so a right-click near the bottom-right corner
      // does not open a menu half off the page.
      menu.hidden = false;
      menu.style.left = "0px";
      menu.style.top = "0px";
      var w = menu.offsetWidth, h = menu.offsetHeight;
      var maxX = window.scrollX + document.documentElement.clientWidth - w - 8;
      var maxY = window.scrollY + document.documentElement.clientHeight - h - 8;
      menu.style.left = Math.max(window.scrollX + 8, Math.min(x, maxX)) + "px";
      menu.style.top  = Math.max(window.scrollY + 8, Math.min(y, maxY)) + "px";
    }
  }

  /* ======================================================================
     The drawer — the same notes, on a narrow screen

     The header of this file promised a drawer and there was not one: below
     980px the layer was hidden and the notes simply vanished from the
     chapter, reachable only from the notes page. This is that list. Plain
     textareas, no dragging, no colours to fiddle with on a phone.
     ====================================================================== */

  var drawer = null;

  function buildDrawer() {
    if (drawer) return;
    drawer = document.createElement("div");
    drawer.hidden = true;
    drawer.className = "note-drawer";
    drawer.innerHTML =
      '<div class="note-drawer-scrim"></div>' +
      '<div class="note-drawer-panel" role="dialog" aria-label="Notes on this chapter">' +
        '<div class="note-drawer-head">' +
          "<strong>Notes on this chapter</strong>" +
          '<button type="button" class="note-drawer-x" title="Close">×</button>' +
        "</div>" +
        '<div class="note-drawer-list"></div>' +
        '<button type="button" class="btn note-drawer-add">📌 New note</button>' +
      "</div>";
    document.body.appendChild(drawer);

    drawer.querySelector(".note-drawer-scrim").addEventListener("click", closeDrawer);
    drawer.querySelector(".note-drawer-x").addEventListener("click", closeDrawer);
    drawer.querySelector(".note-drawer-add").addEventListener("click", function () {
      addForDrawer("");
      fillDrawer();
      var last = drawer.querySelector(".note-drawer-item:last-child textarea");
      if (last) last.focus();
    });
    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape" && drawer && !drawer.hidden) closeDrawer();
    });
  }

  function addForDrawer(q) {
    var existing = window.LF.notesFor(current).length;
    window.LF.addNote({
      ch: current,
      chTitle: chapterTitle(),
      quote: q || "",
      text: "",
      color: window.LF.NOTE_COLORS[existing % window.LF.NOTE_COLORS.length],
      x: 0.6,
      y: window.scrollY + 120,
      z: topZ + 1,
    });
  }

  function fillDrawer() {
    if (!drawer) return;
    var list = drawer.querySelector(".note-drawer-list");
    var notes = window.LF.notesFor(current);

    if (!notes.length) {
      list.innerHTML = '<p class="note-drawer-empty">Nothing here yet. Write down the thing ' +
                       "you would have said out loud.</p>";
      return;
    }

    list.innerHTML = notes.map(function (n) {
      return '<div class="note-drawer-item sticky-' + (n.color || "yellow") + '" data-id="' + n.id + '">' +
        (n.quote ? "<blockquote>" + esc(n.quote) + "</blockquote>" : "") +
        '<textarea rows="3" placeholder="Write it in your own words…">' + esc(n.text) + "</textarea>" +
        '<button type="button" class="note-drawer-del">Delete</button>' +
        "</div>";
    }).join("");

    var areas = list.querySelectorAll("textarea");
    for (var i = 0; i < areas.length; i++) {
      (function (a) {
        var id = a.parentNode.getAttribute("data-id");
        var t = null;
        a.addEventListener("input", function () {
          if (t) clearTimeout(t);
          t = setTimeout(function () { window.LF.updateNote(id, { text: a.value }); }, 400);
        });
        a.addEventListener("blur", function () {
          if (t) clearTimeout(t);
          window.LF.updateNote(id, { text: a.value });
        });
      })(areas[i]);
    }

    var dels = list.querySelectorAll(".note-drawer-del");
    for (var k = 0; k < dels.length; k++) {
      dels[k].addEventListener("click", function () {
        var id = this.parentNode.getAttribute("data-id");
        var all = window.LF.notesFor(current);
        for (var j = 0; j < all.length; j++) {
          if (all[j].id === id) { offerUndo(snapshot(all[j])); break; }
        }
        window.LF.removeNote(id);
        fillDrawer();
      });
    }
  }

  function openDrawer(quote) {
    buildDrawer();
    if (quote) addForDrawer(quote);
    fillDrawer();
    drawer.hidden = false;
    document.body.classList.add("note-drawer-open");
    var area = drawer.querySelector(".note-drawer-item:last-child textarea");
    if (area) area.focus();
  }

  function closeDrawer() {
    if (!drawer) return;
    drawer.hidden = true;
    document.body.classList.remove("note-drawer-open");
    paintButton();
  }

  /* ======================================================================
     Top bar button and keyboard
     ====================================================================== */

  var btn;

  function savePref() {
    try { localStorage.setItem("logiflow.notes.hidden", visible ? "0" : "1"); } catch (e) { /* ignore */ }
  }

  function toggleVisible() {
    visible = !visible;
    savePref();
    render();
    toast(visible ? "Notes shown" : "Notes hidden — press H, or shift-click the pin, to bring them back");
  }

  function paintButton() {
    if (!btn) return;
    var n = isChapter ? window.LF.notesFor(current).length : window.LF.profile.notes.length;
    btn.innerHTML = "📌" + (n ? '<span class="btn-count">' + n + "</span>" : "");
    btn.classList.toggle("off", isChapter && !visible && !!n);
    btn.title = isChapter
      ? (n ? n + " note" + (n === 1 ? "" : "s") + " on this chapter — click to add another (N), " +
             "shift-click to " + (visible ? "hide" : "show") + " them (H)"
           : "Add a sticky note to this chapter (N)")
      : "Your notes";
  }

  function addButton() {
    var bar = document.querySelector(".topbar");
    if (!bar) return;
    btn = document.createElement("button");
    btn.className = "btn btn-note";
    btn.type = "button";
    var theme = document.getElementById("themeToggle");
    if (theme) bar.insertBefore(btn, theme); else bar.appendChild(btn);

    btn.addEventListener("click", function (e) {
      if (!isChapter) { location.href = rel("notes.html"); return; }
      if (isNarrow()) { openDrawer(""); return; }
      if (e.shiftKey) { toggleVisible(); return; }
      create("");
    });
    paintButton();
  }

  /** Nothing fires while you are typing — into a note, a search box or a quiz. */
  function typing(e) {
    var t = e.target;
    if (!t) return false;
    if (t.isContentEditable) return true;
    var tag = t.tagName;
    return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT";
  }

  function keys() {
    /* learn.js owns a "g then d/r/e/n/h/…" chord that jumps to a study page,
       and two of its second keys are the two this file wants. Without this
       mirror of its 1200ms window, "g n" would leave a stray empty note on
       the chapter on its way to the notes page. */
    var armed = false, armedTimer = null;

    document.addEventListener("keydown", function (e) {
      if ((e.ctrlKey || e.metaKey) && !e.shiftKey && (e.key === "z" || e.key === "Z")) {
        if (typing(e)) return;                  // that Ctrl+Z belongs to the text
        if (undoLast()) e.preventDefault();
        return;
      }
      if (e.ctrlKey || e.metaKey || e.altKey || typing(e)) { armed = false; return; }

      // Whatever this key is, it ends the chord — the only question is whether
      // it was the SECOND key of one, in which case it is not ours.
      var chorded = armed;
      armed = false;
      if (armedTimer) clearTimeout(armedTimer);

      if (e.key === "g" || e.key === "G") {
        armed = true;
        armedTimer = setTimeout(function () { armed = false; }, 1200);
        return;
      }
      if (chorded) return;

      if (e.key === "n" || e.key === "N") { e.preventDefault(); create(""); return; }
      if (e.key === "h" || e.key === "H") {
        if (!window.LF.notesFor(current).length) return;
        e.preventDefault();
        toggleVisible();
      }
    });
  }

  /* ---------------------------------------------------------------- go */

  addButton();
  if (isChapter) {
    render();
    selectionBubble();
    contextMenu();
    keys();
    window.addEventListener("scroll", railOnScroll, { passive: true });
    // Notes are placed against document coordinates, so a resize that changes
    // the column width has to move them — and a window widened past 980px has
    // to hand the notes back from the drawer to the layer.
    var resizeTimer = null;
    window.addEventListener("resize", function () {
      if (resizeTimer) clearTimeout(resizeTimer);
      resizeTimer = setTimeout(function () {
        render();
        if (drawer && !drawer.hidden && !isNarrow()) closeDrawer();
      }, 200);
    });
  }

  window.LF.on("note", function () {
    paintButton();
    // Rebuilding the list under the reader's cursor would take the focus away
    // mid-sentence — and every keystroke in the drawer emits this event.
    if (drawer && !drawer.hidden && !drawer.contains(document.activeElement)) fillDrawer();
  });

  window.LFNotes = { create: create, render: render, tidy: tidy, undo: undoLast };
})();
