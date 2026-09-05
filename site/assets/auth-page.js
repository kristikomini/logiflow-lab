/* ==========================================================================
   auth-page.js — the four account pages.

     signin.html    sign in
     register.html  create an account
     reset.html     forgotten password, in two steps
     account.html   the area riservata

   The top-bar panel in account.js still signs you in without losing your place
   and is staying. It deliberately does NOT register or reset: both of those end
   by handing you a recovery code you have to write down, and a popover that
   closes when you click the page behind it is the wrong place for that.

   Everything here is a renderer over window.LFAccount. There is no second copy
   of the auth logic: login, register, forgotPassword, resetPassword, logout and
   sync all live in account.js and this file only draws them. Loaded AFTER
   account.js, on those four pages only.
   ========================================================================== */
(function () {
  "use strict";

  if (!window.LF || !window.LFAccount) return;

  var A  = window.LFAccount;
  var LF = window.LF;

  function esc(s) {
    return String(s === undefined || s === null ? "" : s)
      .replace(/&/g, "&amp;").replace(/</g, "&lt;")
      .replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  /* ======================================================================
     Where to go afterwards

     ?next=dashboard.html lets a link say "sign in and then come back here".
     It is also, in every other product that has one, the open-redirect bug: a
     redirect parameter that accepts an absolute URL turns a link to OUR
     sign-in page into a link to somebody else's, wearing our address bar. So
     the accepted shape is one page inside this site and nothing else — no
     scheme, no host, no "..", no second slash.
     ====================================================================== */

  var SAFE_NEXT = /^(chapters\/)?[A-Za-z0-9][A-Za-z0-9._-]*\.html$/;

  function nextPage(fallback) {
    var m = /[?&]next=([^&]*)/.exec(location.search);
    var raw = "";
    try { raw = m ? decodeURIComponent(m[1]) : ""; } catch (e) { raw = ""; }
    if (!raw || raw.indexOf("..") !== -1 || !SAFE_NEXT.test(raw)) return fallback || "account.html";
    return raw;
  }

  /** Carries ?next= from one auth page to the other, so the round trip survives. */
  function withNext(page) {
    var m = /[?&]next=([^&]*)/.exec(location.search);
    return m ? page + "?next=" + m[1] : page;
  }

  function go(url) { location.href = url; }

  /**
   * Focus the first field without scrolling the heading off the screen.
   *
   * A plain focus() scrolls the field into view, which on these pages means landing
   * past the title and the paragraph explaining what an account is for. preventScroll
   * is honoured everywhere current and ignored harmlessly where it is not.
   */
  function focusQuietly(input) {
    if (!input) return;
    try { input.focus({ preventScroll: true }); } catch (e) { input.focus(); }
  }

  /* ======================================================================
     Small shared pieces
     ====================================================================== */

  function say(node, text, kind) {
    if (!node) return;
    node.textContent = text || "";
    node.className = "auth-alert " + (kind || "");
    node.hidden = !text;
  }

  function initials(name, username) {
    var source = String(name || username || "?").trim();
    var words = source.split(/[\s@._-]+/).filter(function (w) { return w.length; });
    if (!words.length) return "?";
    if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
    return (words[0].charAt(0) + words[1].charAt(0)).toUpperCase();
  }

  function when(iso, empty) {
    if (!iso) return empty || "never";
    var d = new Date(iso);
    if (isNaN(d.getTime())) return empty || "never";
    return d.toLocaleDateString() + " at " + d.toLocaleTimeString();
  }

  /**
   * The address of the API, and a way to change it.
   *
   * Served over http(s) this is the page's own origin and there is nothing to
   * configure. Opened from disk there is no origin to guess, so the site works
   * offline until somebody types one in — which is a normal state, not an
   * error, and the wording has to say so.
   */
  function serverRow() {
    var b = A.base();
    return '<div class="server-row">' +
      (b ? "Server: <code>" + esc(b) + "</code>"
         : "<strong>No server.</strong> Progress is saved in this browser only.") +
      '<span class="spacer"></span>' +
      (b ? '<button type="button" class="btn" data-act="ping">Test it</button> ' : "") +
      '<button type="button" class="btn" data-act="server">' +
        (b ? "Change&hellip;" : "Set an address&hellip;") + "</button>" +
      "</div>";
  }

  function wireServer(root, alertNode) {
    root.addEventListener("click", function (e) {
      var act = e.target.getAttribute && e.target.getAttribute("data-act");
      if (act !== "server" && act !== "ping") return;
      e.preventDefault();

      if (act === "server") {
        var v = window.prompt(
          "Address of the LogiFlow Academy API.\n\n" +
          "Run it with:  dotnet run --project src/LogiFlow.Academy.Api\n" +
          "Leave it empty to work offline in this browser only.\n\n" +
          "Example: http://localhost:5280", A.base());
        if (v === null) return;
        A.setBase(v);
        location.reload();
        return;
      }

      say(alertNode, "Asking the server…", "work");
      A.health().then(function (h) {
        say(alertNode, "The server answered: " + (h && h.status) +
          ", storing progress in " + (h && h.provider) + ".", "ok");
      }, function (err) {
        say(alertNode, "No answer from " + A.base() + " — " + (err.message || "unreachable") +
          ". Is it running, and is this address listed in Academy:AllowedOrigins?", "err");
      });
    });
  }

  /** Turns each password field's little "Show" button into one that works. */
  function wireReveal(root) {
    var buttons = root.querySelectorAll(".reveal");
    for (var i = 0; i < buttons.length; i++) {
      buttons[i].addEventListener("click", function () {
        var input = this.parentNode.querySelector("input");
        if (!input) return;
        var hidden = input.type === "password";
        input.type = hidden ? "text" : "password";
        this.textContent = hidden ? "Hide" : "Show";
        input.focus();
      });
    }
  }

  function passwordField(id, label, autocomplete) {
    return '<div class="field field-pw">' +
      '<label for="' + id + '">' + label + "</label>" +
      '<input id="' + id + '" type="password" autocomplete="' + autocomplete + '" ' +
        'required minlength="10" spellcheck="false">' +
      '<button type="button" class="reveal">Show</button>' +
      "</div>";
  }

  function usernameField(id, label) {
    return '<div class="field"><label for="' + id + '">' + label + "</label>" +
      '<input id="' + id + '" type="text" autocomplete="username" required ' +
        'spellcheck="false" autocapitalize="none" maxlength="32"></div>';
  }

  /** Shown on signin/register when somebody is already signed in on this browser. */
  function alreadyIn(host, verb) {
    var u = A.state.user || {};
    host.innerHTML = '<div class="auth-card"><div class="id-card">' +
      '<div class="id-avatar">' + esc(initials(u.displayName, u.username)) + "</div>" +
      '<div class="id-who"><div class="id-name">' + esc(u.displayName || u.username) + "</div>" +
      '<div class="id-mail">@' + esc(u.username || "") + "</div></div></div>" +
      '<p class="auth-alt">You are already signed in, so there is nothing to ' + verb +
      ". Progress made on this machine is being merged into that account automatically.</p>" +
      '<div class="auth-actions">' +
        '<a class="btn primary big" href="account.html">Your area riservata</a>' +
        '<a class="btn" href="dashboard.html">Dashboard</a>' +
        '<button type="button" class="btn" data-act="logout">Sign out</button>' +
      "</div></div>";

    host.addEventListener("click", function (e) {
      if (e.target.getAttribute && e.target.getAttribute("data-act") === "logout") {
        A.logout().then(function () { location.reload(); });
      }
    });
  }

  /** Shown when no API address is set — the file:// case, and not an error. */
  function noServer(host, what) {
    host.innerHTML = '<div class="auth-card">' +
      "<h2>There is no server to " + what + " on</h2>" +
      "<p>This page was opened straight from disk, or no address has been set, so there is " +
      "nothing to sign in to. <strong>Nothing is broken.</strong> Every chapter, test, card " +
      "and note works and is already saved in this browser; an account only adds a second " +
      "device.</p>" +
      '<div class="code-head">Start one</div>' +
      "<pre><code>dotnet run --project src/LogiFlow.Academy.Api</code></pre>" +
      '<p class="field-hint">That serves this same site <em>and</em> the API on ' +
      "<code>http://localhost:5280</code>, with no database to install. Open the site from " +
      "there and the address below fills itself in.</p>" +
      serverRow() +
      '<p class="auth-alert" id="authMsg" hidden></p>' +
      "</div>";
    wireServer(host, host.querySelector("#authMsg"));
  }

  /* ======================================================================
     The recovery code, and the one moment it exists

     Registration and a completed reset each hand one back. The server keeps
     only its SHA-256, so this screen is the single opportunity anybody will
     ever have to read it — which is why Continue stays disabled until the box
     is ticked. That checkbox is not ceremony for its own sake; it is the
     cheapest defence against the failure that actually happens, which is
     somebody clicking straight past this and losing the account in April.
     ====================================================================== */

  function recoveryCard(code, headline, lead) {
    return '<div class="auth-card recovery">' +
      '<div class="recovery-badge">🔑</div>' +
      "<h2>" + headline + "</h2>" +
      "<p>" + lead + "</p>" +
      '<div class="recovery-code" id="recCode">' + esc(code) + "</div>" +
      '<div class="auth-actions">' +
        '<button type="button" class="btn" data-act="copy-code">Copy</button>' +
        '<button type="button" class="btn" data-act="save-code">Download as a file</button>' +
      "</div>" +
      '<p class="auth-alert" id="recMsg" hidden></p>' +
      '<div class="box trap" style="margin:1.2em 0 0">' +
        '<div class="box-title">⚠ It will not be shown again</div>' +
        "<p>The server keeps only a hash of it, exactly as it does for your password, so it " +
        "genuinely cannot show you this a second time. Without it a forgotten password means " +
        "a lost account &mdash; there is no reset email here, because there is no mail " +
        "transport at all.</p>" +
        "<p>Put it in your password manager beside the password. Using it to recover the " +
        "account spends it, and a fresh one is issued on the way out.</p>" +
      "</div>" +
      '<label class="recovery-ack"><input type="checkbox" id="recAck"> ' +
        "<span>I have saved this code somewhere I will still have it in six months.</span>" +
      "</label>" +
      '<div class="auth-actions">' +
        '<button type="button" class="btn primary big" id="recGo" disabled>Continue</button>' +
      "</div>" +
      "</div>";
  }

  function wireRecovery(host, code, onContinue) {
    var ack = host.querySelector("#recAck");
    var button = host.querySelector("#recGo");
    var msg = host.querySelector("#recMsg");

    ack.addEventListener("change", function () { button.disabled = !ack.checked; });
    button.addEventListener("click", function () { onContinue(); });

    host.addEventListener("click", function (e) {
      var act = e.target.getAttribute && e.target.getAttribute("data-act");
      if (act !== "copy-code" && act !== "save-code") return;
      e.preventDefault();

      if (act === "copy-code") {
        var done = function () { say(msg, "Copied to the clipboard.", "ok"); };
        var failed = function () { say(msg, "Could not copy — select it and copy by hand.", "err"); };
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(code).then(done, failed);
        } else {
          failed();
        }
        return;
      }

      var who = (A.state.user && A.state.user.username) || "account";
      var text =
        "LogiFlow Academy — recovery code\r\n" +
        "================================\r\n\r\n" +
        "Username:       " + who + "\r\n" +
        "Recovery code:  " + code + "\r\n" +
        "Issued:         " + new Date().toString() + "\r\n" +
        "Server:         " + (A.base() || "(none)") + "\r\n\r\n" +
        "This code is the only way back into the account if the password is\r\n" +
        "forgotten. It works once; recovering with it issues a replacement.\r\n" +
        "Anyone holding it can take the account over, so keep it as carefully\r\n" +
        "as the password itself.\r\n";

      var blob = new Blob([text], { type: "text/plain" });
      var a = document.createElement("a");
      a.href = URL.createObjectURL(blob);
      a.download = "logiflow-recovery-" + who + ".txt";
      document.body.appendChild(a);
      a.click();
      setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
      say(msg, "Downloaded. Move it somewhere that will outlive this laptop.", "ok");
    });
  }

  /* ======================================================================
     Sign in
     ====================================================================== */

  function mountSignIn(host) {
    if (!host) return;
    if (A.state.user) { alreadyIn(host, "do"); return; }
    if (!A.base())    { noServer(host, "sign in"); return; }

    host.innerHTML = '<div class="auth-card">' +
      "<h2>Welcome back</h2>" +
      '<p class="auth-alt" style="margin-top:0">Signing in pulls your account’s progress ' +
      "down and merges it with whatever is already in this browser. Nothing is thrown away " +
      "by that: the merge keeps the better of every field.</p>" +
      '<form id="authForm" novalidate>' +
        usernameField("inUser", "Username") +
        passwordField("inPass", "Password", "current-password") +
        '<div class="auth-actions">' +
          '<button type="submit" class="btn primary big" id="inGo">Sign in</button>' +
          '<a class="btn" href="' + withNext("register.html") + '">Create an account</a>' +
        "</div>" +
        '<p class="auth-alert" id="authMsg" hidden></p>' +
      "</form>" +
      serverRow() +
      "</div>" +
      '<p class="auth-alt"><a href="' + withNext("reset.html") + '">Forgotten your password?</a>' +
      " &mdash; the recovery code you were given when you signed up will get you back in.</p>";

    var msg = host.querySelector("#authMsg");
    wireReveal(host);
    wireServer(host, msg);

    host.querySelector("#authForm").addEventListener("submit", function (e) {
      e.preventDefault();
      var username = host.querySelector("#inUser").value.trim();
      var pass     = host.querySelector("#inPass").value;
      if (!username || !pass) { say(msg, "Fill in both fields.", "err"); return; }

      var button = host.querySelector("#inGo");
      button.disabled = true;
      say(msg, "Signing in and merging your progress…", "work");

      A.login(username, pass).then(function () {
        say(msg, "Signed in. Taking you through…", "ok");
        go(nextPage("account.html"));
      }, function (err) {
        button.disabled = false;
        say(msg, err.message || "Could not sign in.", "err");
        host.querySelector("#inPass").focus();
      });
    });

    focusQuietly(host.querySelector("#inUser"));
  }

  /* ======================================================================
     Create an account
     ====================================================================== */

  /**
   * A length-and-variety score, 0–4. Deliberately not a password policy.
   *
   * The server enforces exactly one rule — ten characters — and this meter does
   * not invent more. Composition rules ("one capital, one digit, one symbol")
   * are what produce Password1! on every account in the building; length is the
   * property that actually costs an attacker time.
   */
  function strength(value) {
    var v = value || "";
    if (v.length < 10) return { score: 0, text: "Too short — ten characters is the minimum." };
    var classes = (/[a-z]/.test(v) ? 1 : 0) + (/[A-Z]/.test(v) ? 1 : 0) +
                  (/[0-9]/.test(v) ? 1 : 0) + (/[^A-Za-z0-9]/.test(v) ? 1 : 0);
    if (v.length >= 20) return { score: 4, text: "Long. That is the property that actually matters." };
    if (v.length >= 16) return { score: 3, text: "Good length." };
    if (v.length >= 12 || classes >= 3) return { score: 2, text: "Acceptable. Longer beats more exotic." };
    return { score: 1, text: "Long enough to be accepted, and not much more." };
  }

  /**
   * The username rule, mirroring Usernames.cs on the server.
   *
   * The server checks this again and its answer is the one that counts — this copy exists
   * only so the answer is instant. Two implementations of one rule is a real risk of drift,
   * and the mitigation is that both sides say so and both are tested.
   */
  var USERNAME_SHAPE = /^[a-z0-9][a-z0-9._-]{1,30}[a-z0-9]$/;
  var USERNAME_RULE = "Use 3 to 32 characters: letters, digits, and . _ - in the middle.";

  function usernameProblem(raw) {
    var u = String(raw || "").trim().toLowerCase();
    if (!u) return "Choose a username.";
    if (u.indexOf("@") !== -1) return "No @ — this is a username, not an email address.";
    if (!USERNAME_SHAPE.test(u)) return USERNAME_RULE;
    return "";
  }

  function mountRegister(host) {
    if (!host) return;
    if (A.state.user) { alreadyIn(host, "create"); return; }
    if (!A.base())    { noServer(host, "create an account"); return; }

    var carried = LF.mastery();
    var noteCount = LF.profile.notes.length;

    host.innerHTML = '<div class="auth-card">' +
      "<h2>Create an account</h2>" +
      '<p class="auth-alt" style="margin-top:0">' +
        (carried.percent > 0
          ? "Everything you have done so far — <strong>" + carried.percent +
            "% mastered</strong>, " + LF.profile.xp + " XP, " + noteCount +
            " note" + (noteCount === 1 ? "" : "s") +
            " — is uploaded to the new account the moment it exists. Nothing restarts."
          : "There is no progress in this browser yet, so the new account starts empty.") +
      "</p>" +
      '<form id="authForm" novalidate>' +
        usernameField("upUser", "Username") +
        '<p class="field-hint" id="upUserHint">' + USERNAME_RULE + "</p>" +

        '<div class="field"><label for="upName">Display name ' +
          '<span style="font-weight:400">(optional)</span></label>' +
        '<input id="upName" type="text" autocomplete="nickname" maxlength="60">' +
        '<p class="field-hint">Shown on the leaderboard, where you can also hide yourself ' +
        "entirely. Left empty it is just your username.</p></div>" +

        passwordField("upPass", "Password", "new-password") +
        '<div class="pw-meter" id="upMeter" aria-hidden="true"><i></i><i></i><i></i><i></i></div>' +
        '<p class="field-hint" id="upStrength">At least ten characters. A short sentence you ' +
        "will remember beats a short mess you will not.</p>" +

        '<div class="field field-pw"><label for="upPass2">Password again</label>' +
        '<input id="upPass2" type="password" autocomplete="new-password" required spellcheck="false">' +
        '<button type="button" class="reveal">Show</button>' +
        '<p class="field-hint" id="upMatch"></p></div>' +

        '<div class="auth-actions">' +
          '<button type="submit" class="btn primary big" id="upGo">Create account</button>' +
          '<a class="btn" href="' + withNext("signin.html") + '">I already have one</a>' +
        "</div>" +
        '<p class="auth-alert" id="authMsg" hidden></p>' +
      "</form>" +
      serverRow() +
      "</div>";

    var msg   = host.querySelector("#authMsg");
    var user  = host.querySelector("#upUser");
    var uhint = host.querySelector("#upUserHint");
    var pass  = host.querySelector("#upPass");
    var pass2 = host.querySelector("#upPass2");
    var meter = host.querySelector("#upMeter");
    var note  = host.querySelector("#upStrength");
    var match = host.querySelector("#upMatch");

    var DEFAULT_HINT = "At least ten characters. A short sentence you will remember beats a " +
      "short mess you will not.";

    wireReveal(host);
    wireServer(host, msg);

    user.addEventListener("input", function () {
      var problem = user.value ? usernameProblem(user.value) : "";
      uhint.textContent = problem || USERNAME_RULE;
      uhint.className = "field-hint" + (problem ? " bad" : "");
      user.setAttribute("aria-invalid", problem ? "true" : "false");
    });

    function paintMatch() {
      if (!pass2.value) {
        match.textContent = "";
        match.className = "field-hint";
        pass2.removeAttribute("aria-invalid");
        return;
      }
      var same = pass.value === pass2.value;
      match.textContent = same ? "They match." : "These two do not match.";
      match.className = "field-hint " + (same ? "good" : "bad");
      pass2.setAttribute("aria-invalid", same ? "false" : "true");
    }

    function paintStrength() {
      var s = strength(pass.value);
      var bars = meter.querySelectorAll("i");
      for (var i = 0; i < bars.length; i++) {
        bars[i].className = i < s.score ? "on" + s.score : "";
      }
      note.textContent = pass.value ? s.text : DEFAULT_HINT;
      note.className = "field-hint" + (pass.value && s.score === 0 ? " bad" : "");
      paintMatch();
    }

    pass.addEventListener("input", paintStrength);
    pass2.addEventListener("input", paintMatch);

    host.querySelector("#authForm").addEventListener("submit", function (e) {
      e.preventDefault();
      var username = user.value.trim();
      var name     = host.querySelector("#upName").value.trim();

      // The server checks all of this again. These run first only so the answer is
      // instant, not because the browser is trusted to do the checking.
      var problem = usernameProblem(username);
      if (problem) { say(msg, problem, "err"); user.focus(); return; }
      if (pass.value.length < 10) { say(msg, "Use at least 10 characters.", "err"); pass.focus(); return; }
      if (pass.value !== pass2.value) { say(msg, "The two passwords do not match.", "err"); pass2.focus(); return; }

      var button = host.querySelector("#upGo");
      button.disabled = true;
      say(msg, "Creating the account and uploading your progress…", "work");

      A.register(username, name, pass.value).then(function (code) {
        host.innerHTML = recoveryCard(
          code,
          "Save this recovery code",
          "The account exists and your progress is already uploaded. One thing left, and it " +
          "is the one nobody can do for you afterwards.");
        wireRecovery(host, code, function () { go(nextPage("account.html")); });
        host.scrollIntoView({ block: "start" });
      }, function (err) {
        button.disabled = false;
        say(msg, err.message || "Could not create that account.", "err");
      });
    });

    focusQuietly(user);
  }

  /* ======================================================================
     Forgotten password, in two steps

     Step one proves the account is yours and gets a ticket; step two spends the
     ticket on a new password. That is the shape every reset flow has — the
     emailed link IS step one's ticket — and keeping the two halves apart here,
     where the proof is a recovery code rather than a mailbox, means the parts
     line up with the ones you would have to name in an interview.

     The ticket lives in a closure and nowhere else. It is a credential that can
     change a password, so putting it in localStorage or in the URL would leave
     it lying around for exactly as long as it is dangerous.
     ====================================================================== */

  function mountReset(host) {
    if (!host) return;
    if (!A.base()) { noServer(host, "reset a password"); return; }

    var ticket = "";

    function stepOne() {
      host.innerHTML = '<div class="auth-card">' +
        '<p class="auth-steps"><span class="on">1 &middot; Prove it is yours</span>' +
        "<span>2 &middot; Choose a new password</span></p>" +
        "<h2>Recover your account</h2>" +
        '<p class="auth-alt" style="margin-top:0">Type the recovery code you were given when ' +
        "you created the account. Capitals, spaces and dashes make no difference.</p>" +
        '<form id="authForm" novalidate>' +
          usernameField("rsUser", "Username") +
          '<div class="field"><label for="rsCode">Recovery code</label>' +
          '<input id="rsCode" type="text" autocomplete="one-time-code" required ' +
            'spellcheck="false" autocapitalize="characters" class="code-input" ' +
            'placeholder="K7M2Q-3XZ9F-P4WRT-8NBHV">' +
          '<p class="field-hint">Twenty characters, usually written in four groups.</p></div>' +
          '<div class="auth-actions">' +
            '<button type="submit" class="btn primary big" id="rsGo">Continue</button>' +
            '<a class="btn" href="' + withNext("signin.html") + '">Back to sign in</a>' +
          "</div>" +
          '<p class="auth-alert" id="authMsg" hidden></p>' +
        "</form>" +
        serverRow() +
        "</div>" +
        '<div class="box trap">' +
          '<div class="box-title">⚠ If you do not have the code</div>' +
          "<p>Then this account cannot be recovered, and pretending otherwise would be a back " +
          "door with a friendly form around it. There is no mail transport here, so there is " +
          "nothing that could verify you by another route &mdash; and a reset that verifies " +
          "nothing is not a reset, it is a way for anyone to take anyone's account.</p>" +
          "<p>Your work is not lost. It is still in this browser: open " +
          '<a href="account.html">the area riservata</a> and export a backup, then ' +
          '<a href="register.html">create a new account</a> and import it there.</p>' +
        "</div>";

      var msg = host.querySelector("#authMsg");
      wireServer(host, msg);

      host.querySelector("#authForm").addEventListener("submit", function (e) {
        e.preventDefault();
        var username = host.querySelector("#rsUser").value.trim();
        var code     = host.querySelector("#rsCode").value.trim();
        if (!username || !code) { say(msg, "Fill in both fields.", "err"); return; }

        var button = host.querySelector("#rsGo");
        button.disabled = true;
        say(msg, "Checking…", "work");

        A.forgotPassword(username, code).then(function (payload) {
          ticket = (payload && payload.resetToken) || "";
          stepTwo(Math.round(((payload && payload.expiresInSeconds) || 900) / 60));
          host.scrollIntoView({ block: "start" });
        }, function (err) {
          button.disabled = false;
          say(msg, err.message || "That did not match an account.", "err");
        });
      });

      focusQuietly(host.querySelector("#rsUser"));
    }

    function stepTwo(minutes) {
      host.innerHTML = '<div class="auth-card">' +
        '<p class="auth-steps"><span class="done">1 &middot; Proved</span>' +
        '<span class="on">2 &middot; Choose a new password</span></p>' +
        "<h2>Choose a new password</h2>" +
        '<p class="auth-alt" style="margin-top:0">This reset is good for about ' + minutes +
        " minutes, works once, and signs you out everywhere else &mdash; which is the point, " +
        "if the reason you are here is that somebody else got in.</p>" +
        '<form id="authForm" novalidate>' +
          passwordField("rsPass", "New password", "new-password") +
          '<div class="pw-meter" id="rsMeter" aria-hidden="true"><i></i><i></i><i></i><i></i></div>' +
          '<p class="field-hint" id="rsStrength">At least ten characters.</p>' +
          '<div class="field field-pw"><label for="rsPass2">New password again</label>' +
          '<input id="rsPass2" type="password" autocomplete="new-password" required spellcheck="false">' +
          '<button type="button" class="reveal">Show</button>' +
          '<p class="field-hint" id="rsMatch"></p></div>' +
          '<div class="auth-actions">' +
            '<button type="submit" class="btn primary big" id="rsSet">Set the new password</button>' +
          "</div>" +
          '<p class="auth-alert" id="authMsg" hidden></p>' +
        "</form>" +
        "</div>";

      var msg   = host.querySelector("#authMsg");
      var pass  = host.querySelector("#rsPass");
      var pass2 = host.querySelector("#rsPass2");
      var meter = host.querySelector("#rsMeter");
      var note  = host.querySelector("#rsStrength");
      var match = host.querySelector("#rsMatch");

      wireReveal(host);

      pass.addEventListener("input", function () {
        var s = strength(pass.value);
        var bars = meter.querySelectorAll("i");
        for (var i = 0; i < bars.length; i++) bars[i].className = i < s.score ? "on" + s.score : "";
        note.textContent = pass.value ? s.text : "At least ten characters.";
        note.className = "field-hint" + (pass.value && s.score === 0 ? " bad" : "");
      });

      pass2.addEventListener("input", function () {
        if (!pass2.value) { match.textContent = ""; match.className = "field-hint"; return; }
        var same = pass.value === pass2.value;
        match.textContent = same ? "They match." : "These two do not match.";
        match.className = "field-hint " + (same ? "good" : "bad");
      });

      host.querySelector("#authForm").addEventListener("submit", function (e) {
        e.preventDefault();
        if (pass.value.length < 10) { say(msg, "Use at least 10 characters.", "err"); pass.focus(); return; }
        if (pass.value !== pass2.value) { say(msg, "The two passwords do not match.", "err"); pass2.focus(); return; }

        var button = host.querySelector("#rsSet");
        button.disabled = true;
        say(msg, "Setting it…", "work");

        A.resetPassword(ticket, pass.value).then(function (code) {
          ticket = "";
          host.innerHTML = recoveryCard(
            code,
            "Done — and here is your new code",
            "Your password is changed and you are signed in. The old recovery code was spent " +
            "getting you here, so this one replaces it.");
          wireRecovery(host, code, function () { go(nextPage("account.html")); });
          host.scrollIntoView({ block: "start" });
        }, function (err) {
          button.disabled = false;
          say(msg, err.message || "Could not set that password.", "err");
        });
      });

      focusQuietly(pass);
    }

    stepOne();
  }

  /* ======================================================================
     Area riservata
     ====================================================================== */

  function gate(host) {
    host.innerHTML = '<div class="auth-card auth-gate">' +
      '<div class="gate-lock">🔒</div>' +
      "<h2>You are not signed in</h2>" +
      "<p>This page is the one part of the site that needs an account. Everything else " +
      "— all the chapters, the tests, the review deck, the mock exam, your notes " +
      "— works exactly the same signed out, and is already saved in this browser.</p>" +
      '<div class="auth-actions">' +
        '<a class="btn primary big" href="signin.html?next=account.html">Sign in</a>' +
        '<a class="btn big" href="register.html?next=account.html">Create an account</a>' +
      "</div>" +
      '<p class="auth-alt"><a href="reset.html?next=account.html">Forgotten your password?</a></p>' +
      "</div>" +
      '<div class="auth-card">' + serverRow() +
      '<p class="auth-alert" id="authMsg" hidden></p></div>';
    wireServer(host, host.querySelector("#authMsg"));
  }

  function statGrid() {
    var m   = LF.mastery();
    var lv  = LF.level();
    var s   = LF.profile.streak;
    var due = LF.due().length;

    function stat(num, label, footnote) {
      return '<div class="stat"><div class="stat-num">' + num + "</div>" +
        '<div class="stat-label">' + label + "</div>" +
        (footnote ? '<div class="stat-note">' + footnote + "</div>" : "") + "</div>";
    }

    return '<div class="stat-grid">' +
      stat(m.percent + "%", "Mastered", m.tested + " of " + m.total + " chapters passed") +
      stat(LF.profile.xp, "XP", esc(lv.name)) +
      stat(s.count, "Day streak", "Best " + s.best) +
      stat(due, "Cards due", due ? '<a href="review.html">Review them</a>' : "Nothing waiting") +
      stat(LF.profile.notes.length, "Notes", '<a href="notes.html">Open them</a>') +
      stat(LF.profile.examBest ? Math.round(LF.profile.examBest * 100) + "%" : "—",
           "Best mock exam", LF.profile.examCount + " sat") +
      "</div>";
  }

  function mountAccount(host) {
    if (!host) return;
    if (!A.state.user) { gate(host); return; }

    var u = A.state.user;

    function render() {
      var dot = A.state.status === "error" ? "err"
              : A.state.status === "signed-in" ? "on" : "";

      host.innerHTML =
        '<div class="auth-card"><div class="id-card">' +
          '<div class="id-avatar">' + esc(initials(u.displayName, u.username)) + "</div>" +
          '<div class="id-who">' +
            '<div class="id-name">' + esc(u.displayName || u.username) + "</div>" +
            '<div class="id-mail">@' + esc(u.username || "") + "</div>" +
            '<div class="id-state"><span class="sync-dot ' + dot + '"></span>' +
              esc(A.state.message || (A.state.status === "signed-in"
                ? "Syncing automatically" : A.state.status)) + "</div>" +
          "</div></div>" +
          '<div class="auth-actions">' +
            '<button type="button" class="btn primary" data-act="syncnow">Sync now</button>' +
            '<a class="btn" href="dashboard.html">Dashboard</a>' +
            '<button type="button" class="btn" data-act="logout">Sign out</button>' +
          "</div>" +
          '<p class="auth-alert" id="authMsg" hidden></p>' +
        "</div>" +

        statGrid() +

        '<div class="auth-card">' +
          "<h2>The account</h2>" +
          '<dl class="kv-list">' +
            '<div class="kv"><dt>Username</dt><dd><code>' + esc(u.username || "—") + "</code></dd></div>" +
            '<div class="kv"><dt>Display name</dt><dd>' + esc(u.displayName || "—") + "</dd></div>" +
            '<div class="kv"><dt>User id</dt><dd><code>' + esc(u.id || "—") + "</code></dd></div>" +
            '<div class="kv"><dt>Last synced</dt><dd>' +
              esc(when(LF.profile.remote.syncedAt, "not yet in this session")) + "</dd></div>" +
            '<div class="kv"><dt>Studying since</dt><dd>' +
              esc(when(LF.profile.createdAt, "today")) + "</dd></div>" +
            '<div class="kv"><dt>This browser</dt><dd>' +
              (LF.memoryOnly()
                ? "refusing to store data — progress here lasts until the tab closes"
                : "keeping a local copy as well, so the site works with the server down") +
              "</dd></div>" +
          "</dl>" +
          '<p class="field-hint">No email address, anywhere. This service cannot send mail, ' +
          "so an address could never be verified or written to &mdash; it would be personal " +
          "data collected for nothing, which is exactly what data minimisation is about.</p>" +
          serverRow() +
        "</div>" +

        '<div class="auth-card">' +
          "<h2>Your recovery code</h2>" +
          "<p>The code you were given when you signed up is the only way back in if you forget " +
          "your password. The server keeps a hash of it and nothing else, so it cannot be " +
          "looked up or re-sent &mdash; but it can be <strong>replaced</strong>, which is what " +
          "to do if you never wrote it down, or think somebody else has seen it.</p>" +
          '<div class="auth-actions">' +
            '<button type="button" class="btn" data-act="newcode">Issue a new code</button>' +
            '<a class="btn" href="reset.html">Change my password</a>' +
          "</div>" +
          '<p class="field-hint">A new code <em>replaces</em> the old one immediately: two ' +
          "live codes would double the number of secrets that can take the account over, and " +
          "you would have no way to tell which one had leaked. Changing your password goes " +
          "through the same recovery page and signs you out everywhere else.</p>" +
          '<div id="codeSlot"></div>' +
          '<p class="auth-alert" id="codeMsg" hidden></p>' +
        "</div>" +

        '<div class="auth-card">' +
          "<h2>The leaderboard</h2>" +
          "<p>Your display name, XP, mastery and streak are visible to everyone else using " +
          "this deployment. The figures are re-derived by the server from your own progress " +
          "document rather than taken from the browser’s word for it — but anyone " +
          "can edit their local profile before syncing, so it is a nudge, not a ranking.</p>" +
          '<div class="auth-actions">' +
            '<button type="button" class="btn" data-act="hide">Hide me</button>' +
            '<button type="button" class="btn" data-act="show">Show me again</button>' +
            '<a class="btn" href="dashboard.html#leaderboard">See the board</a>' +
          "</div>" +
          '<p class="auth-alert" id="boardMsg" hidden></p>' +
        "</div>" +

        '<div class="auth-card">' +
          "<h2>Your data</h2>" +
          "<p>The whole of your progress is one JSON document. The server stores it verbatim " +
          "and never looks inside, except to read the handful of numbers the leaderboard " +
          "needs. Export it whenever you like; it is yours.</p>" +
          '<div class="auth-actions">' +
            '<button type="button" class="btn" data-act="export">Export a backup</button>' +
            '<button type="button" class="btn" data-act="import">Import a backup</button>' +
            '<button type="button" class="btn danger" data-act="reset">Clear this browser</button>' +
          "</div>" +
          '<p class="field-hint">Clearing while signed in only empties <em>this browser</em>: ' +
          "the next sync merges your account’s copy straight back down, because the merge " +
          "takes the better of every field and an empty profile is never the better one. To " +
          "start over for real, sign out first, then clear.</p>" +
          '<p class="auth-alert" id="dataMsg" hidden></p>' +
        "</div>";

      wireServer(host, host.querySelector("#authMsg"));
    }

    /* One delegated handler for the lot, bound once. render() replaces the whole
       innerHTML on each state change, so per-button listeners would not survive it.
       The recovery card mounted into #codeSlot brings its own handler, so its two
       actions are skipped here rather than handled twice. */
    host.addEventListener("click", function (e) {
      var act = e.target.getAttribute && e.target.getAttribute("data-act");
      if (!act || act === "server" || act === "ping" ||
          act === "copy-code" || act === "save-code") return;
      e.preventDefault();

      /* Every alert element is looked up at CALL time, never captured up here.
         render() replaces the whole innerHTML on each state change, so a node held
         across an await is detached by the time the reply arrives — and writing to a
         detached node is a message the user never sees. "Sync now" is exactly that
         case: A.sync() flips the status to "syncing" and re-renders before it
         resolves. */
      function tell(id, text, kind) { say(host.querySelector("#" + id), text, kind); }

      if (act === "logout") {
        A.logout().then(function () { location.reload(); });
      }

      if (act === "syncnow") {
        tell("authMsg", "Syncing…", "work");
        A.sync(true).then(function (pushed) {
          tell("authMsg", pushed ? "Synced." : (A.state.message || "Already up to date."), "ok");
        });
      }

      if (act === "newcode") {
        if (!window.confirm(
          "Issue a new recovery code?\n\n" +
          "The one you have now stops working immediately. Do this only if you have lost it, " +
          "or think somebody else has seen it.")) return;

        tell("codeMsg", "Asking the server…", "work");
        A.newRecoveryCode().then(function (code) {
          tell("codeMsg", "", "");
          var slot = host.querySelector("#codeSlot");
          slot.innerHTML = recoveryCard(
            code,
            "Your new recovery code",
            "The previous one stopped working the moment this appeared. Save it before you " +
            "leave the page.");
          // Continue only tidies the card away here — there is nowhere else to send you.
          wireRecovery(slot, code, function () { slot.innerHTML = ""; });
          slot.scrollIntoView({ block: "center" });
        }, function (err) {
          tell("codeMsg", err.message || "Could not issue a new code.", "err");
        });
      }

      if (act === "hide" || act === "show") {
        tell("boardMsg", "Asking the server…", "work");
        A.setLeaderboardVisible(act === "show").then(function () {
          tell("boardMsg", act === "show"
            ? "You are on the leaderboard again."
            : "Hidden. Your progress still syncs; the board just stops listing you.", "ok");
        }, function (err) {
          tell("boardMsg", err.message || "Could not change that.", "err");
        });
      }

      if (act === "export") {
        var blob = new Blob([LF.exportJson()], { type: "application/json" });
        var a = document.createElement("a");
        a.href = URL.createObjectURL(blob);
        a.download = "logiflow-progress-" + LF.today() + ".json";
        document.body.appendChild(a);
        a.click();
        setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 1000);
        tell("dataMsg", "Downloaded.", "ok");
      }

      if (act === "import") {
        var input = document.createElement("input");
        input.type = "file";
        input.accept = "application/json,.json";
        input.addEventListener("change", function () {
          var f = input.files && input.files[0];
          if (!f) return;
          var reader = new FileReader();
          reader.onload = function () {
            try {
              LF.importJson(String(reader.result));
              tell("dataMsg", "Imported. Reloading…", "ok");
              setTimeout(function () { location.reload(); }, 700);
            } catch (err) {
              tell("dataMsg", "That file is not a LogiFlow profile.", "err");
            }
          };
          reader.readAsText(f);
        });
        input.click();
      }

      if (act === "reset") {
        if (!window.confirm(
          "Delete all progress, scores, cards, notes and badges in THIS BROWSER?\n\n" +
          "You are signed in, so the next sync will pull your account's copy back down. " +
          "Sign out first if you meant to start over completely.")) return;
        LF.reset();
        location.reload();
      }
    });

    /* Redraw whenever the sync state moves, so "Syncing…" and the timestamp stay live.
       Skipped while a recovery code is on screen: that card holds the only copy of a
       secret, and a background sync must not wipe it away from under somebody who is
       halfway through writing it down. */
    A.onChange(function () {
      if (!A.state.user) { location.reload(); return; }
      var slot = host.querySelector("#codeSlot");
      if (slot && slot.firstChild) return;
      render();
    });

    render();
    A.sync(false);
  }

  window.LFAuthPage = {
    mountSignIn: mountSignIn,
    mountRegister: mountRegister,
    mountReset: mountReset,
    mountAccount: mountAccount,
    nextPage: nextPage,
    strength: strength,
    usernameProblem: usernameProblem,
  };
})();
