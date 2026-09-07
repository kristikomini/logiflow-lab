/* ==========================================================================
   italiano-panel.js — renders the Italian layer.

   Two jobs, one file, because they share the data in italiano.js:

     1. On a CHAPTER page, appends an "In italiano" panel with the one or two
        sentences you would actually say about that chapter's core idea. It
        appears only where an entry exists — a half-written translation teaches
        a sentence you would not want to say, so there is no filler.

     2. On italiano.html, renders the whole phrasebook.

   Why sentences rather than a word list: the glossary already covers
   vocabulary. Knowing that "query" stays "query" does not help you produce
   "dipende dal caso d'uso: se il carico è in lettura conviene…" at speed under
   pressure. Having said it once does.
   ========================================================================== */
(function () {
  "use strict";

  function esc(s) {
    return String(s == null ? "" : s)
      .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  /* ------------------------------------------------------------ the panel */

  function chapterPanel() {
    var id = document.body.getAttribute("data-chapter");
    if (!id || !window.IT_CHAPTERS) return;

    var entry = window.IT_CHAPTERS[id];
    if (!entry) return;

    var main = document.querySelector("main.main");
    if (!main) return;

    var pager = document.getElementById("pager");

    var box = document.createElement("div");
    box.className = "box it-box";
    box.innerHTML =
      '<div class="box-title">🇮🇹 In italiano</div>' +
      "<p class=\"it-lede\">The sentences you would actually say about this chapter in a " +
      "colloquio. Read them out loud once &mdash; that is the whole exercise.</p>" +
      entry.say.map(function (s) {
        return '<p class="it-say" lang="it">' + esc(s) + "</p>";
      }).join("") +
      (entry.keep && entry.keep.length
        ? '<p class="it-keep"><strong>Stays in English:</strong> ' +
          entry.keep.map(function (k) { return "<code>" + esc(k) + "</code>"; }).join(" ") +
          "</p>"
        : "") +
      '<p class="it-more"><a href="../italiano.html">The full phrasebook &rarr;</a></p>';

    if (pager) main.insertBefore(box, pager);
    else main.appendChild(box);
  }

  /* -------------------------------------------------------- the phrasebook */

  function phrasebook() {
    var host = document.getElementById("phrasebook");
    if (!host || !window.IT_PHRASES) return;

    host.innerHTML = window.IT_PHRASES.map(function (group) {
      return '<section class="it-group"><h2 id="' +
        esc(group.group.toLowerCase().replace(/[^a-z]+/g, "-")) + '">' +
        esc(group.group) + "</h2>" +
        group.items.map(function (p) {
          return '<div class="it-phrase">' +
            '<p class="it-say" lang="it">' + esc(p.it) + "</p>" +
            '<p class="it-en">' + esc(p.en) + "</p>" +
            (p.note ? '<p class="it-note">' + esc(p.note) + "</p>" : "") +
            "</div>";
        }).join("") +
        "</section>";
    }).join("");

    /* The chapter lines, collected, so the phrasebook is the one page to
       revise from the night before. */
    var byChapter = document.getElementById("chapterLines");
    if (!byChapter || !window.IT_CHAPTERS || !window.CHAPTERS) return;

    var covered = window.CHAPTERS.filter(function (c) { return window.IT_CHAPTERS[c.id]; });

    byChapter.innerHTML =
      "<p>" + covered.length + " of " + window.CHAPTERS.length +
      " chapters have Italian lines so far. The panel appears on those chapters only &mdash; " +
      "see <a href=\"README.md\">site/README.md</a> to add more.</p>" +
      covered.map(function (c) {
        var e = window.IT_CHAPTERS[c.id];
        return '<section class="it-group"><h3><a href="chapters/' + esc(c.id) + '.html">' +
          esc(c.n) + " &middot; " + esc(c.title) + "</a></h3>" +
          e.say.map(function (s) { return '<p class="it-say" lang="it">' + esc(s) + "</p>"; }).join("") +
          (e.keep ? '<p class="it-keep">' + e.keep.map(function (k) {
            return "<code>" + esc(k) + "</code>";
          }).join(" ") + "</p>" : "") +
          "</section>";
      }).join("");
  }

  chapterPanel();
  phrasebook();
})();
