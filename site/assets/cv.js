/* ==========================================================================
   cv.js — the CV and cover-letter builder.

   WHY A BUILDER AND NOT A TEMPLATE
   Chapter 36 already tells you what an Italian technical CV needs: the GDPR
   line, CEFR language levels, one page as a junior, PDF named
   CV_Cognome_Nome.pdf, and bullets that say what changed rather than what you
   were present for. Knowing all of that and still shipping a bad CV is the
   normal outcome, because the rules are easy to agree with and easy to forget
   at 23:00 the night before an application closes.

   So this page does two things a chapter cannot:

     1. It PRODUCES the document, in the shape the chapter argues for, from
        fields you fill in once and keep.

     2. It CHECKS the document against the chapter's own rules and tells you
        which ones you broke. That is the part worth having — a linter for your
        CV. Every warning here corresponds to a specific paragraph in chapter
        36, and none of them are stylistic opinions.

   OUTPUT
   Print to PDF (Ctrl+P) using the print stylesheet, or copy as Markdown for a
   plain-text application form. There is no server, nothing is uploaded, and
   everything lives in this browser's localStorage under the same key as the
   rest of your profile data.

   WHAT THIS IS NOT
   It is not an AI that writes your CV. The bullets are yours; it can only tell
   you when one of them contains no facts.
   ========================================================================== */
(function () {
  "use strict";

  var host = document.getElementById("cvApp");
  if (!host) return;

  var KEY = "logiflow.cv.v1";

  var GDPR =
    "Autorizzo il trattamento dei miei dati personali ai sensi del " +
    "Regolamento UE 2016/679 (GDPR).";

  var BLANK = {
    lang: "it",
    name: "",
    role: "Sviluppatore .NET",
    email: "",
    phone: "",
    city: "",
    linkedin: "",
    github: "",
    summary: "",
    skills: "C#, .NET 10, ASP.NET Core, EF Core, SQL Server, T-SQL, Git, Docker",
    experience: [],
    education: [],
    projects: [],
    languages: [
      { name: "Italiano", level: "C1" },
      { name: "Inglese", level: "B2" },
    ],
    letterCompany: "",
    letterRole: "",
    letterBody: "",
  };

  var cv = load();

  /* ------------------------------------------------------------------ store */

  function load() {
    try {
      var raw = localStorage.getItem(KEY);
      if (!raw) return JSON.parse(JSON.stringify(BLANK));
      var parsed = JSON.parse(raw);
      Object.keys(BLANK).forEach(function (k) {
        if (parsed[k] === undefined) parsed[k] = JSON.parse(JSON.stringify(BLANK[k]));
      });
      return parsed;
    } catch (e) {
      /* Same rule as store.js: a private window must degrade, never break. */
      return JSON.parse(JSON.stringify(BLANK));
    }
  }

  function save() {
    try { localStorage.setItem(KEY, JSON.stringify(cv)); } catch (e) { /* memory only */ }
  }

  function esc(s) {
    return String(s == null ? "" : s)
      .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  /* ==========================================================================
     The linter — every rule traceable to chapter 36
     ========================================================================== */

  /* Words that describe presence rather than result. A bullet built from these
     and nothing else is the "worked on the backend" bullet the chapter opens
     with. Both languages, because people mix them. */
  var PRESENCE = [
    "worked on", "responsible for", "involved in", "helped with", "assisted",
    "partecipato", "collaborato", "mi sono occupato", "occupata di", "supporto a",
  ];

  function lint() {
    var issues = [];

    function add(severity, text, where) {
      issues.push({ severity: severity, text: text, where: where });
    }

    if (!cv.name) add("error", "No name. The file is named after it.", "Details");
    if (!cv.email) add("error", "No email address.", "Details");
    if (!cv.city) add("warn", "No city. Employers in Modena filter on commuting distance.", "Details");

    /* The GDPR line is not optional in practice — some applicant systems filter
       on its absence. It is added automatically for the Italian version, so
       this only fires on the English one. */
    if (cv.lang === "en") {
      add("note",
        "English version: the GDPR authorisation line is omitted, which is right for a " +
        "multinational and wrong for an Italian employer. Keep both.", "Language");
    }

    var bullets = [];
    cv.experience.concat(cv.projects).forEach(function (e) {
      (e.bullets || "").split("\n").forEach(function (b) {
        if (b.trim()) bullets.push({ text: b.trim(), where: e.title || e.role || "Experience" });
      });
    });

    if (!bullets.length) {
      add("error", "No bullets anywhere. A CV with no bullets is a list of places you have been.", "Experience");
    }

    var withNumbers = 0;
    bullets.forEach(function (b) {
      var lower = b.text.toLowerCase();

      /* "Have a number wherever one honestly exists." Chapter 36. */
      if (/\d/.test(b.text)) withNumbers++;

      PRESENCE.forEach(function (p) {
        if (lower.indexOf(p) !== -1) {
          add("warn",
            "“" + b.text.slice(0, 60) + "…” describes presence, not result. " +
            "What did you do → with what → what changed?", b.where);
        }
      });

      if (b.text.length > 220) {
        add("warn", "One bullet is over 220 characters. It will wrap to four lines and be skipped.", b.where);
      }
    });

    if (bullets.length && withNumbers === 0) {
      add("warn",
        "Not one bullet contains a number. Measured ones only — but “4 s to 90 ms” " +
        "is the difference between a claim and a fact.", "Experience");
    }

    cv.languages.forEach(function (l) {
      if (l.level && !/^[ABC][12]$/.test(l.level.trim())) {
        add("warn",
          "“" + esc(l.level) + "” is not a CEFR level. “Good” means nothing; " +
          "B2 is a claim they can test.", "Languages");
      }
    });

    /* One page as a junior. A rough character budget rather than a real
       measurement — the printed preview is the actual test, and this is the
       nudge that makes you look at it. */
    var chars = JSON.stringify(cv).length;
    if (chars > 4200) {
      add("warn",
        "This is running long for one page. As a junior, one page — cut the oldest thing, " +
        "not the newest.", "Length");
    }

    if (!cv.summary) {
      add("note", "No summary. Three lines at the top is where a recruiter spends six seconds.", "Summary");
    }

    return issues;
  }

  /* ==========================================================================
     Rendering
     ========================================================================== */

  function field(label, key, type, placeholder) {
    return '<label class="cv-field"><span>' + esc(label) + "</span>" +
      (type === "textarea"
        ? '<textarea rows="3" data-k="' + key + '" placeholder="' + esc(placeholder || "") + '">' + esc(cv[key]) + "</textarea>"
        : '<input type="' + (type || "text") + '" data-k="' + key + '" value="' + esc(cv[key]) +
          '" placeholder="' + esc(placeholder || "") + '">') +
      "</label>";
  }

  function listBlock(title, key, fields) {
    var rows = (cv[key] || []).map(function (item, i) {
      return '<div class="cv-row" data-list="' + key + '" data-i="' + i + '">' +
        fields.map(function (f) {
          return '<label class="cv-field"><span>' + esc(f.label) + "</span>" +
            (f.type === "textarea"
              ? '<textarea rows="3" data-f="' + f.key + '" placeholder="' + esc(f.placeholder || "") + '">' + esc(item[f.key] || "") + "</textarea>"
              : '<input data-f="' + f.key + '" value="' + esc(item[f.key] || "") + '" placeholder="' + esc(f.placeholder || "") + '">') +
            "</label>";
        }).join("") +
        '<button class="btn cv-del" data-del="' + key + '" data-i="' + i + '">Remove</button>' +
        "</div>";
    }).join("");

    return '<section class="cv-block"><h3>' + esc(title) + "</h3>" + rows +
      '<button class="btn" data-add="' + key + '">Add</button></section>';
  }

  function renderEditor() {
    return '<div class="cv-editor">' +
      '<section class="cv-block"><h3>Details</h3>' +
        '<label class="cv-field"><span>Language of the document</span>' +
          '<select data-k="lang">' +
            '<option value="it"' + (cv.lang === "it" ? " selected" : "") + '>Italiano — for an Italian employer</option>' +
            '<option value="en"' + (cv.lang === "en" ? " selected" : "") + '>English — for a multinational</option>' +
          "</select></label>" +
        field("Full name", "name", "text", "Mario Rossi") +
        field("Target role", "role", "text", "Sviluppatore .NET") +
        field("Email", "email", "email", "mario.rossi@example.com") +
        field("Phone", "phone", "tel", "+39 333 1234567") +
        field("City", "city", "text", "Modena") +
        field("LinkedIn", "linkedin", "text", "linkedin.com/in/mariorossi") +
        field("GitHub", "github", "text", "github.com/mariorossi") +
      "</section>" +

      '<section class="cv-block"><h3>Summary</h3>' +
        field("Three lines, no more", "summary", "textarea",
          "Sviluppatore .NET con esperienza su ASP.NET Core, EF Core e SQL Server…") +
      "</section>" +

      '<section class="cv-block"><h3>Skills</h3>' +
        field("Comma-separated", "skills", "textarea") +
      "</section>" +

      listBlock("Experience", "experience", [
        { key: "role", label: "Role" },
        { key: "org", label: "Company" },
        { key: "dates", label: "Dates", placeholder: "2024 – oggi" },
        { key: "bullets", label: "Bullets (one per line)", type: "textarea",
          placeholder: "Costruita una Web API in ASP.NET Core: 28 endpoint, JWT, 120 test." },
      ]) +

      listBlock("Projects", "projects", [
        { key: "title", label: "Project" },
        { key: "url", label: "Link" },
        { key: "bullets", label: "Bullets (one per line)", type: "textarea" },
      ]) +

      listBlock("Education", "education", [
        { key: "title", label: "Qualification" },
        { key: "org", label: "Institution" },
        { key: "dates", label: "Dates" },
      ]) +

      listBlock("Languages", "languages", [
        { key: "name", label: "Language" },
        { key: "level", label: "CEFR level", placeholder: "B2" },
      ]) +

      '<section class="cv-block"><h3>Lettera di presentazione</h3>' +
        field("Company", "letterCompany", "text") +
        field("Role as advertised", "letterRole", "text") +
        field("Body — three short paragraphs", "letterBody", "textarea",
          "Perché loro · cosa porto io · una prova concreta") +
      "</section>" +
    "</div>";
  }

  function renderPreview() {
    var it = cv.lang === "it";

    var contact = [cv.city, cv.email, cv.phone, cv.linkedin, cv.github]
      .filter(Boolean).map(esc).join(" &middot; ");

    function section(title, body) {
      return body ? "<h2>" + esc(title) + "</h2>" + body : "";
    }

    function entries(list, primary, secondary) {
      return (list || []).map(function (e) {
        var bullets = (e.bullets || "").split("\n").filter(function (b) { return b.trim(); });
        return '<div class="cv-entry">' +
          "<h3>" + esc(e[primary] || "") +
          (e[secondary] ? ' <span class="cv-org">&mdash; ' + esc(e[secondary]) + "</span>" : "") +
          (e.dates ? '<span class="cv-dates">' + esc(e.dates) + "</span>" : "") +
          "</h3>" +
          (e.url ? '<p class="cv-url">' + esc(e.url) + "</p>" : "") +
          (bullets.length ? "<ul>" + bullets.map(function (b) { return "<li>" + esc(b) + "</li>"; }).join("") + "</ul>" : "") +
          "</div>";
      }).join("");
    }

    return '<article class="cv-paper" id="cvPaper">' +
      "<h1>" + esc(cv.name || (it ? "Nome Cognome" : "Your Name")) + "</h1>" +
      '<p class="cv-role">' + esc(cv.role) + "</p>" +
      '<p class="cv-contact">' + contact + "</p>" +

      (cv.summary ? "<p class=\"cv-summary\">" + esc(cv.summary) + "</p>" : "") +

      section(it ? "Competenze" : "Skills",
        cv.skills ? '<p class="cv-skills">' + esc(cv.skills) + "</p>" : "") +

      section(it ? "Esperienza" : "Experience", entries(cv.experience, "role", "org")) +
      section(it ? "Progetti" : "Projects", entries(cv.projects, "title", "")) +
      section(it ? "Formazione" : "Education", entries(cv.education, "title", "org")) +

      section(it ? "Lingue" : "Languages",
        (cv.languages || []).length
          ? "<p>" + cv.languages.filter(function (l) { return l.name; })
              .map(function (l) { return esc(l.name) + " " + esc(l.level); }).join(" &middot; ") + "</p>"
          : "") +

      /* Added automatically for the Italian document, because its absence is
         noticed and some systems filter on it. Chapter 36. */
      (it ? '<p class="cv-gdpr">' + esc(GDPR) + "</p>" : "") +
    "</article>" +

    (cv.letterBody
      ? '<article class="cv-paper cv-letter" id="cvLetter">' +
          "<h1>" + esc(it ? "Lettera di presentazione" : "Cover letter") + "</h1>" +
          "<p>" + esc(cv.letterCompany) + (cv.letterRole ? " &mdash; " + esc(cv.letterRole) : "") + "</p>" +
          cv.letterBody.split(/\n{2,}/).map(function (p) { return "<p>" + esc(p) + "</p>"; }).join("") +
          "<p>" + esc(cv.name) + "</p>" +
        "</article>"
      : "");
  }

  function renderLint() {
    var issues = lint();
    if (!issues.length) {
      return '<div class="box"><div class="box-title">✓ Nothing to flag</div>' +
        "<p>Every rule from <a href=\"chapters/36-cv-and-linkedin.html\">chapter 36</a> that " +
        "can be checked mechanically is satisfied. The ones that cannot — whether the bullets " +
        "are <em>true</em>, and whether they are the right three — are still yours.</p></div>";
    }

    var order = { error: 0, warn: 1, note: 2 };
    issues.sort(function (a, b) { return order[a.severity] - order[b.severity]; });

    return '<div class="box trap"><div class="box-title">⚠ ' + issues.length +
      " thing" + (issues.length === 1 ? "" : "s") + " chapter 36 would object to</div><ul class=\"cv-lint\">" +
      issues.map(function (i) {
        return '<li class="lint-' + i.severity + '"><strong>' + esc(i.where) + "</strong> " + i.text + "</li>";
      }).join("") + "</ul></div>";
  }

  function toMarkdown() {
    var it = cv.lang === "it";
    var out = ["# " + cv.name, "", cv.role, "",
      [cv.city, cv.email, cv.phone, cv.linkedin, cv.github].filter(Boolean).join(" · "), ""];

    if (cv.summary) out.push(cv.summary, "");
    if (cv.skills) out.push("## " + (it ? "Competenze" : "Skills"), "", cv.skills, "");

    function block(title, list, primary, secondary) {
      if (!list || !list.length) return;
      out.push("## " + title, "");
      list.forEach(function (e) {
        out.push("### " + (e[primary] || "") + (e[secondary] ? " — " + e[secondary] : "") +
          (e.dates ? "  (" + e.dates + ")" : ""));
        (e.bullets || "").split("\n").filter(function (b) { return b.trim(); })
          .forEach(function (b) { out.push("- " + b.trim()); });
        out.push("");
      });
    }

    block(it ? "Esperienza" : "Experience", cv.experience, "role", "org");
    block(it ? "Progetti" : "Projects", cv.projects, "title", "url");
    block(it ? "Formazione" : "Education", cv.education, "title", "org");

    if (cv.languages.length) {
      out.push("## " + (it ? "Lingue" : "Languages"), "",
        cv.languages.filter(function (l) { return l.name; })
          .map(function (l) { return l.name + " " + l.level; }).join(" · "), "");
    }

    if (it) out.push("", "_" + GDPR + "_");

    return out.join("\n");
  }

  /* ==========================================================================
     Wiring
     ========================================================================== */

  function paint() {
    host.innerHTML =
      '<div class="cv-actions">' +
        '<button class="btn primary" id="cvPrint">Print / save as PDF</button> ' +
        '<button class="btn" id="cvMd">Copy as Markdown</button> ' +
        '<button class="btn" id="cvReset">Start over</button>' +
        '<span class="cv-filename">Save it as <code>CV_' +
          esc((cv.name || "Cognome Nome").split(" ").reverse().join("_")) +
        '.pdf</code></span>' +
      "</div>" +
      renderLint() +
      '<div class="cv-split">' + renderEditor() + '<div class="cv-preview">' + renderPreview() + "</div></div>";

    wire();
  }

  function wire() {
    /* One delegated input handler for the whole form. Repainting on every
       keystroke would move the caret, so the preview is updated in place and
       the editor is left alone. */
    host.addEventListener("input", onInput);
    host.addEventListener("change", onInput);
    host.addEventListener("click", onClick);
  }

  function onInput(e) {
    var t = e.target;

    if (t.hasAttribute("data-k")) {
      cv[t.getAttribute("data-k")] = t.value;
    } else if (t.hasAttribute("data-f")) {
      var row = t.closest(".cv-row");
      cv[row.getAttribute("data-list")][+row.getAttribute("data-i")][t.getAttribute("data-f")] = t.value;
    } else {
      return;
    }

    save();
    refreshPreviewOnly();
  }

  function refreshPreviewOnly() {
    var preview = host.querySelector(".cv-preview");
    if (preview) preview.innerHTML = renderPreview();

    var lintBox = host.querySelector(".box");
    if (lintBox) lintBox.outerHTML = renderLint();
  }

  function onClick(e) {
    var add = e.target.closest("[data-add]");
    if (add) {
      cv[add.getAttribute("data-add")].push({});
      save();
      paint();
      return;
    }

    var del = e.target.closest("[data-del]");
    if (del) {
      cv[del.getAttribute("data-del")].splice(+del.getAttribute("data-i"), 1);
      save();
      paint();
      return;
    }

    if (e.target.id === "cvPrint") {
      window.print();
      return;
    }

    if (e.target.id === "cvMd") {
      var md = toMarkdown();
      if (navigator.clipboard) navigator.clipboard.writeText(md);
      e.target.textContent = "Copied";
      setTimeout(function () { e.target.textContent = "Copy as Markdown"; }, 1400);
      return;
    }

    if (e.target.id === "cvReset") {
      if (!confirm("Clear everything in the CV builder? This cannot be undone.")) return;
      cv = JSON.parse(JSON.stringify(BLANK));
      save();
      paint();
    }
  }

  paint();
})();
