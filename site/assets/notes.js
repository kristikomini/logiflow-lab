/* ==========================================================================
   notes.js — sticky notes you can leave on a chapter.

   Three ways to make one:

     * the "+ Note" button in the top bar, which drops a blank note into view
     * selecting text in the chapter, which offers to quote it into a note
     * right-clicking anywhere in the chapter body, which opens a small menu
       and drops the note at the pointer — no selection required

   Notes are absolutely positioned against the DOCUMENT, not the viewport, so
   a note pinned next to the paragraph about deferred execution is still next
   to that paragraph tomorrow. The position is stored as a fraction of the
   main column's width plus an absolute y, so the note stays roughly where it
   was put when the window is a different size — perfect fidelity across
   layouts is not achievable without anchoring to the DOM, and anchoring to
   the DOM breaks the moment a chapter is edited.

   Below 980px the layer is hidden entirely and notes are shown as a plain
   list in a drawer, because dragging small cards around a phone screen is
   nobody's idea of studying.
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

  /** Must match .sticky { width } in learn.css. */
  var CARD_W = 296;

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

  function render() {
    if (!isChapter) return;
    ensureLayer();
    layer.innerHTML = "";
    var notes = window.LF.notesFor(current);
    notes.forEach(function (n) { layer.appendChild(build(n)); });
    layer.hidden = !visible || !notes.length;
    paintButton();
  }

  function build(n) {
    var card = document.createElement("div");
    card.className = "sticky sticky-" + (n.color || "yellow");
    card.setAttribute("data-id", n.id);

    // Keep the whole card on screen: a note whose text is off the right edge
    // is worse than one that has drifted a little from where it was dropped.
    var viewport = document.documentElement.clientWidth;
    var left = mainLeft() + (typeof n.x === "number" ? n.x : 0.7) * mainWidth();
    card.style.left = Math.max(8, Math.min(left, viewport - CARD_W - 12)) + "px";
    card.style.top  = (n.y || 200) + "px";

    var swatches = window.LF.NOTE_COLORS.map(function (c) {
      return '<button type="button" class="sw sw-' + c + (c === n.color ? " on" : "") +
             '" data-color="' + c + '" title="' + c + '"></button>';
    }).join("");

    card.innerHTML =
      '<div class="sticky-bar">' +
        '<span class="sticky-grip" title="Drag">⠿</span>' +
        '<span class="sticky-colors">' + swatches + "</span>" +
        '<button type="button" class="sticky-x" title="Delete this note">×</button>' +
      "</div>" +
      (n.quote ? '<blockquote class="sticky-quote">' + esc(n.quote) + "</blockquote>" : "") +
      '<div class="sticky-text" contenteditable="true" spellcheck="false" ' +
      'data-placeholder="Write it in your own words…">' + esc(n.text) + "</div>";

    wireCard(card, n);
    return card;
  }

  function wireCard(card, n) {
    var text = card.querySelector(".sticky-text");

    var saveTimer = null;
    text.addEventListener("input", function () {
      if (saveTimer) clearTimeout(saveTimer);
      saveTimer = setTimeout(function () {
        window.LF.updateNote(n.id, { text: text.textContent });
      }, 400);
    });
    text.addEventListener("blur", function () {
      window.LF.updateNote(n.id, { text: text.textContent });
    });

    card.querySelector(".sticky-x").addEventListener("click", function () {
      card.classList.add("going");
      setTimeout(function () { window.LF.removeNote(n.id); render(); }, 160);
    });

    var swatches = card.querySelectorAll(".sw");
    for (var i = 0; i < swatches.length; i++) {
      swatches[i].addEventListener("click", function () {
        var c = this.getAttribute("data-color");
        window.LF.updateNote(n.id, { color: c });
        card.className = "sticky sticky-" + c;
        var all = card.querySelectorAll(".sw");
        for (var k = 0; k < all.length; k++) {
          all[k].classList.toggle("on", all[k].getAttribute("data-color") === c);
        }
      });
    }

    drag(card, card.querySelector(".sticky-bar"), n);
  }

  /* ------------------------------------------------------------- dragging */

  function drag(card, handle, n) {
    var startX = 0, startY = 0, originLeft = 0, originTop = 0, moving = false;

    handle.addEventListener("pointerdown", function (e) {
      if (e.target.closest(".sw") || e.target.closest(".sticky-x")) return;
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
      var nx = originLeft + (e.clientX - startX);
      var ny = originTop + (e.clientY - startY);
      nx = Math.max(4, Math.min(nx, document.documentElement.clientWidth - CARD_W - 12));
      ny = Math.max(64, ny);
      card.style.left = nx + "px";
      card.style.top = ny + "px";
    });

    function end(e) {
      if (!moving) return;
      moving = false;
      card.classList.remove("dragging");
      try { handle.releasePointerCapture(e.pointerId); } catch (err) { /* ignore */ }
      window.LF.updateNote(n.id, {
        x: (parseFloat(card.style.left) - mainLeft()) / mainWidth(),
        y: parseFloat(card.style.top),
      });
    }
    handle.addEventListener("pointerup", end);
    handle.addEventListener("pointercancel", end);
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

     Right-clicking the chapter body opens our own one-item menu and drops a
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
          '<button type="button" class="note-menu-item" data-act="all">🗂️ All my notes</button>';
        document.body.appendChild(menu);
        menu.addEventListener("click", function (ev) {
          var b = ev.target.closest(".note-menu-item");
          if (!b) return;
          var act = b.getAttribute("data-act");
          closeMenu();
          if (act === "all") { location.href = rel("notes.html"); return; }
          var s = window.getSelection();
          if (s && menu.dataset.quote) s.removeAllRanges();
          create(menu.dataset.quote || "", { x: +menu.dataset.x, y: +menu.dataset.y });
        });
      }

      menu.dataset.quote = quote || "";
      menu.dataset.x = x;
      menu.dataset.y = y;
      menu.querySelector(".note-menu-label").textContent = quote ? "Note this quote" : "New note here";

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
     Top bar button
     ====================================================================== */

  var btn;

  function savePref() {
    try { localStorage.setItem("logiflow.notes.hidden", visible ? "0" : "1"); } catch (e) { /* ignore */ }
  }

  function paintButton() {
    if (!btn) return;
    var n = isChapter ? window.LF.notesFor(current).length : window.LF.profile.notes.length;
    btn.innerHTML = "📌" + (n ? '<span class="btn-count">' + n + "</span>" : "");
    btn.title = isChapter
      ? (n ? n + " note" + (n === 1 ? "" : "s") + " on this chapter — click to add another, shift-click to hide them"
           : "Add a sticky note to this chapter")
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
      if (e.shiftKey) {
        visible = !visible;
        savePref();
        render();
        return;
      }
      create("");
    });
    paintButton();
  }

  /* ---------------------------------------------------------------- go */

  addButton();
  if (isChapter) {
    render();
    selectionBubble();
    contextMenu();
    // Notes are placed against document coordinates, so a resize that changes
    // the column width has to move them.
    var resizeTimer = null;
    window.addEventListener("resize", function () {
      if (resizeTimer) clearTimeout(resizeTimer);
      resizeTimer = setTimeout(render, 200);
    });
  }

  window.LF.on("note", paintButton);

  window.LFNotes = { create: create, render: render };
})();
