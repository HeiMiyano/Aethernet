namespace Aethernet.Server.AdminDashboard;

/// <summary>
/// Single self-contained HTML page for the web moderation dashboard (stats, open reports,
/// user lookup + ban/unban, audit log). Served at GET /admin/dashboard — see Program.cs.
/// Talks to THIS service's existing /admin REST surface same-origin, and to the auth service's
/// /auth/login cross-origin (hence the Cors:AllowedOrigins + Admin:AuthBaseUrl settings; see
/// docs/DEPLOY.md). No build step, no npm — matches the inline-HTML style already used for
/// AuthController's post-registration success page.
/// </summary>
public static class AdminDashboardHtml
{
    public static string Render(string authBaseUrl)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(authBaseUrl ?? "");
        return Html.Replace("\"__AUTH_BASE_URL__\"", json);
    }

    private const string Html = @"<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Aethernet — Moderation</title>
<style>
  :root { color-scheme: dark light; }
  * { box-sizing: border-box; }
  body {
    font-family: system-ui, -apple-system, sans-serif;
    margin: 0; padding: 0;
    background: #f4f4f7; color: #1a1a1d;
  }
  @media (prefers-color-scheme: dark) { body { background: #121214; color: #eceef0; } }

  header {
    display: flex; align-items: center; justify-content: space-between;
    padding: .9rem 1.5rem; border-bottom: 1px solid rgba(128,128,128,.25);
  }
  header h1 { font-size: 1.05rem; margin: 0; font-weight: 600; }
  header .who { font-size: .82rem; color: #888; }
  header button { background: none; border: 1px solid rgba(128,128,128,.4); color: inherit;
    padding: .35rem .8rem; border-radius: 6px; cursor: pointer; font-size: .82rem; }

  main { max-width: 980px; margin: 0 auto; padding: 1.25rem 1.5rem 3rem; }

  nav.tabs { display: flex; gap: .4rem; margin-bottom: 1.25rem; flex-wrap: wrap; }
  nav.tabs button { background: none; border: 1px solid rgba(128,128,128,.3); color: inherit;
    padding: .45rem 1rem; border-radius: 999px; cursor: pointer; font-size: .85rem; }
  nav.tabs button.active { background: #4a90e2; border-color: #4a90e2; color: white; }

  .cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(130px, 1fr)); gap: .75rem; margin-bottom: 1.5rem; }
  .card { background: rgba(128,128,128,.08); border-radius: 10px; padding: .9rem 1rem; }
  .card .n { font-size: 1.6rem; font-weight: 700; }
  .card .l { font-size: .75rem; color: #888; text-transform: uppercase; letter-spacing: .04em; }

  section.panel { display: none; }
  section.panel.active { display: block; }

  table { width: 100%; border-collapse: collapse; font-size: .85rem; }
  th, td { text-align: left; padding: .5rem .6rem; border-bottom: 1px solid rgba(128,128,128,.15); vertical-align: top; }
  th { color: #888; font-weight: 600; font-size: .75rem; text-transform: uppercase; letter-spacing: .03em; }
  tr:hover td { background: rgba(128,128,128,.06); }

  .row { display: flex; gap: .5rem; margin-bottom: 1rem; flex-wrap: wrap; }
  input[type=text], input[type=password] {
    background: rgba(128,128,128,.08); border: 1px solid rgba(128,128,128,.3); color: inherit;
    padding: .5rem .7rem; border-radius: 6px; font-size: .9rem; flex: 1; min-width: 160px;
  }
  button.btn {
    background: #4a90e2; color: white; border: none; padding: .5rem 1rem;
    border-radius: 6px; cursor: pointer; font-size: .85rem; font-weight: 500;
  }
  button.btn:hover { background: #357ab8; }
  button.btn.danger { background: #d64545; }
  button.btn.danger:hover { background: #b23333; }
  button.btn.ghost { background: none; border: 1px solid rgba(128,128,128,.4); color: inherit; }
  button.btn:disabled { opacity: .5; cursor: default; }

  .pill { display: inline-block; padding: .15rem .55rem; border-radius: 999px; font-size: .72rem; font-weight: 600; }
  .pill.ok   { background: rgba(46,160,67,.18);  color: #2ea043; }
  .pill.bad  { background: rgba(214,69,69,.18);  color: #d64545; }
  .pill.role { background: rgba(74,144,226,.18); color: #4a90e2; margin-left: .3rem; }

  .msg { font-size: .82rem; color: #888; margin-top: .5rem; min-height: 1.2em; }
  .msg.error { color: #d64545; }
  .msg.ok { color: #2ea043; }

  .userbox { background: rgba(128,128,128,.08); border-radius: 10px; padding: 1rem 1.25rem; margin-top: 1rem; }
  .userbox .field { display: flex; justify-content: space-between; padding: .25rem 0; font-size: .88rem; border-bottom: 1px solid rgba(128,128,128,.1); }
  .userbox .field:last-child { border-bottom: none; }
  .userbox .field .k { color: #888; }
  .actions { display: flex; gap: .5rem; margin-top: .9rem; flex-wrap: wrap; }

  #loginScreen { max-width: 360px; margin: 4rem auto; padding: 0 1rem; }
  #loginScreen h1 { font-size: 1.3rem; margin-bottom: .25rem; }
  #loginScreen p { color: #888; font-size: .85rem; margin-bottom: 1.5rem; }
  #loginScreen .row { flex-direction: column; }

  #app { display: none; }
  .empty { color: #888; font-size: .85rem; padding: 1rem 0; }
</style>
</head>
<body>

<div id=""loginScreen"">
  <h1>Aethernet moderation</h1>
  <p>Sign in with an admin or moderator account.</p>
  <div class=""row"">
    <input type=""text"" id=""loginUid"" placeholder=""UID"" autocomplete=""username"">
  </div>
  <div class=""row"">
    <input type=""password"" id=""loginKey"" placeholder=""Secret key"" autocomplete=""current-password"">
  </div>
  <button class=""btn"" id=""loginBtn"" style=""width:100%"">Sign in</button>
  <div class=""msg"" id=""loginMsg""></div>
</div>

<div id=""app"">
  <header>
    <h1>Aethernet moderation</h1>
    <div style=""display:flex; align-items:center; gap:.9rem;"">
      <span class=""who"" id=""whoami""></span>
      <button id=""logoutBtn"">Sign out</button>
    </div>
  </header>
  <main>
    <div class=""cards"" id=""statCards""></div>

    <nav class=""tabs"">
      <button data-tab=""reports"" class=""active"">Reports</button>
      <button data-tab=""users"">Users</button>
      <button data-tab=""audit"">Audit log</button>
    </nav>

    <section class=""panel active"" id=""panel-reports"">
      <div class=""row"">
        <button class=""btn ghost"" id=""reportsShowResolved"">Show resolved</button>
        <button class=""btn ghost"" id=""reportsRefresh"">Refresh</button>
      </div>
      <table>
        <thead><tr><th>Reported</th><th>Reporter</th><th>Reason</th><th>When</th><th></th></tr></thead>
        <tbody id=""reportsBody""></tbody>
      </table>
      <div class=""empty"" id=""reportsEmpty"" style=""display:none"">No open reports.</div>
    </section>

    <section class=""panel"" id=""panel-users"">
      <div class=""row"">
        <input type=""text"" id=""userLookupUid"" placeholder=""UID to look up"">
        <button class=""btn"" id=""userLookupBtn"">Look up</button>
      </div>
      <div id=""userResult""></div>
    </section>

    <section class=""panel"" id=""panel-audit"">
      <div class=""row"">
        <button class=""btn ghost"" id=""auditRefresh"">Refresh</button>
      </div>
      <table>
        <thead><tr><th>When</th><th>Actor</th><th>Action</th><th>Target</th><th>Detail</th></tr></thead>
        <tbody id=""auditBody""></tbody>
      </table>
    </section>
  </main>
</div>

<script>
(function () {
  ""use strict"";
  const AUTH_BASE_URL = ""__AUTH_BASE_URL__"";
  const TOKEN_KEY = ""aethernet_admin_jwt"";

  const $ = (sel, root) => (root || document).querySelector(sel);
  const $$ = (sel, root) => Array.from((root || document).querySelectorAll(sel));

  function token() { return localStorage.getItem(TOKEN_KEY) || """"; }
  function setToken(t) { if (t) localStorage.setItem(TOKEN_KEY, t); else localStorage.removeItem(TOKEN_KEY); }

  function fmtDate(s) {
    if (!s) return ""—"";
    try { return new Date(s).toLocaleString(); } catch { return s; }
  }
  function esc(s) {
    return String(s ?? """").replace(/[&<>""']/g, c => ({ ""&"": ""&amp;"", ""<"": ""&lt;"", "">"": ""&gt;"", '""': ""&quot;"", ""'"": ""&#39;"" }[c]));
  }

  // ---- auth'd fetch against THIS origin's /admin/* surface ----
  async function adminFetch(path, opts) {
    opts = opts || {};
    const headers = Object.assign({ ""Authorization"": ""Bearer "" + token() }, opts.headers || {});
    if (opts.body && !headers[""Content-Type""]) headers[""Content-Type""] = ""application/json"";
    const res = await fetch(path, Object.assign({}, opts, { headers }));
    if (res.status === 401 || res.status === 403) {
      setToken("""");
      showLogin(""Session expired or you don't have moderator access."");
      throw new Error(""unauthorized"");
    }
    if (!res.ok) throw new Error(""request failed: "" + res.status);
    if (res.status === 204) return null;
    const ct = res.headers.get(""content-type"") || """";
    return ct.includes(""json"") ? res.json() : null;
  }

  function showLogin(msg) {
    $(""#app"").style.display = ""none"";
    $(""#loginScreen"").style.display = ""block"";
    $(""#loginMsg"").textContent = msg || """";
    $(""#loginMsg"").className = ""msg"" + (msg ? "" error"" : """");
  }
  function showApp() {
    $(""#loginScreen"").style.display = ""none"";
    $(""#app"").style.display = ""block"";
    $(""#whoami"").textContent = ""signed in"";
    loadStats();
    loadReports();
  }

  // ---- login (cross-origin to the auth service) ----
  $(""#loginBtn"").addEventListener(""click"", async () => {
    const uid = $(""#loginUid"").value.trim();
    const secretKey = $(""#loginKey"").value.trim();
    const msgEl = $(""#loginMsg"");
    msgEl.className = ""msg""; msgEl.textContent = ""Signing in…"";
    if (!uid || !secretKey) { msgEl.className = ""msg error""; msgEl.textContent = ""Enter a UID and secret key.""; return; }
    try {
      const base = AUTH_BASE_URL || """";
      const res = await fetch(base + ""/auth/login"", {
        method: ""POST"",
        headers: { ""Content-Type"": ""application/json"" },
        body: JSON.stringify({ uid: uid, secretKey: secretKey }),
      });
      if (!res.ok) { msgEl.className = ""msg error""; msgEl.textContent = ""Sign-in failed — wrong credentials, or this account isn't admin/moderator.""; return; }
      const data = await res.json();
      setToken(data.accessToken);
      msgEl.textContent = """";
      showApp();
    } catch (e) {
      msgEl.className = ""msg error"";
      msgEl.textContent = ""Couldn't reach the auth service"" + (AUTH_BASE_URL ? """" : "" (no Admin:AuthBaseUrl configured on the server — see docs/DEPLOY.md)"") + ""."";
    }
  });

  $(""#logoutBtn"").addEventListener(""click"", () => { setToken(""""); showLogin(); });

  // ---- tabs ----
  $$(""nav.tabs button"").forEach(btn => {
    btn.addEventListener(""click"", () => {
      $$(""nav.tabs button"").forEach(b => b.classList.remove(""active""));
      btn.classList.add(""active"");
      $$(""section.panel"").forEach(p => p.classList.remove(""active""));
      $(""#panel-"" + btn.dataset.tab).classList.add(""active"");
      if (btn.dataset.tab === ""audit"") loadAudit();
    });
  });

  // ---- stats ----
  async function loadStats() {
    try {
      const s = await adminFetch(""/admin/stats"");
      $(""#statCards"").innerHTML = [
        [""Users"", s.users], [""Banned"", s.bannedUsers], [""Pairs"", s.pairs],
        [""Groups"", s.groups], [""Open reports"", s.openReports],
      ].map(([l, n]) => `<div class=""card""><div class=""n"">${esc(n)}</div><div class=""l"">${esc(l)}</div></div>`).join("""");
    } catch (e) { /* adminFetch already handled auth errors */ }
  }

  // ---- reports ----
  let reportsResolved = false;
  async function loadReports() {
    const rows = await adminFetch(""/admin/reports?resolved="" + reportsResolved + ""&take=200"");
    const body = $(""#reportsBody"");
    if (!rows || rows.length === 0) {
      body.innerHTML = """";
      $(""#reportsEmpty"").style.display = ""block"";
      $(""#reportsEmpty"").textContent = reportsResolved ? ""No resolved reports."" : ""No open reports."";
      return;
    }
    $(""#reportsEmpty"").style.display = ""none"";
    body.innerHTML = rows.map(r => `
      <tr>
        <td><a href=""#"" class=""goto-user"" data-uid=""${esc(r.reported)}"">${esc(r.reported)}</a></td>
        <td>${esc(r.reporter)}</td>
        <td>${esc(r.reason)}</td>
        <td>${esc(fmtDate(r.createdAt))}</td>
        <td>${reportsResolved ? """" : `<button class=""btn ghost resolve-btn"" data-id=""${r.id}"">Resolve</button>`}</td>
      </tr>`).join("""");
    $$("".resolve-btn"", body).forEach(b => b.addEventListener(""click"", async () => {
      b.disabled = true;
      try { await adminFetch(""/admin/reports/"" + b.dataset.id + ""/resolve"", { method: ""POST"", body: JSON.stringify({ note: ""resolved via dashboard"" }) }); loadReports(); loadStats(); }
      catch (e) { b.disabled = false; }
    }));
    $$("".goto-user"", body).forEach(a => a.addEventListener(""click"", (ev) => {
      ev.preventDefault();
      $$(""nav.tabs button"").forEach(b => b.classList.toggle(""active"", b.dataset.tab === ""users""));
      $$(""section.panel"").forEach(p => p.classList.toggle(""active"", p.id === ""panel-users""));
      $(""#userLookupUid"").value = a.dataset.uid;
      lookupUser(a.dataset.uid);
    }));
  }
  $(""#reportsRefresh"").addEventListener(""click"", loadReports);
  $(""#reportsShowResolved"").addEventListener(""click"", (ev) => {
    reportsResolved = !reportsResolved;
    ev.target.textContent = reportsResolved ? ""Show open"" : ""Show resolved"";
    loadReports();
  });

  // ---- users ----
  async function lookupUser(uid) {
    const box = $(""#userResult"");
    box.innerHTML = '<div class=""empty"">Loading…</div>';
    try {
      const u = await adminFetch(""/admin/users/"" + encodeURIComponent(uid));
      renderUser(u);
    } catch (e) {
      box.innerHTML = '<div class=""empty"">No user found with that UID.</div>';
    }
  }
  function renderUser(u) {
    const box = $(""#userResult"");
    const roles = (u.isAdmin ? '<span class=""pill role"">admin</span>' : """") + (u.isModerator ? '<span class=""pill role"">moderator</span>' : """");
    box.innerHTML = `
      <div class=""userbox"">
        <div class=""field""><span class=""k"">UID</span><span>${esc(u.uid)} ${roles}</span></div>
        <div class=""field""><span class=""k"">Alias</span><span>${esc(u.alias) || ""—""}</span></div>
        <div class=""field""><span class=""k"">Status</span><span>${u.isBanned ? '<span class=""pill bad"">banned</span>' : '<span class=""pill ok"">active</span>'}</span></div>
        ${u.isBanned ? `<div class=""field""><span class=""k"">Ban reason</span><span>${esc(u.banReason) || ""—""}</span></div>` : """"}
        <div class=""field""><span class=""k"">Created</span><span>${esc(fmtDate(u.createdAt))}</span></div>
        <div class=""field""><span class=""k"">Last seen</span><span>${esc(fmtDate(u.lastSeenAt))}</span></div>
        <div class=""field""><span class=""k"">Pairs / groups</span><span>${esc(u.pairs)} / ${esc(u.groups)}</span></div>
        <div class=""field""><span class=""k"">Open reports against them</span><span>${esc(u.openReports)}</span></div>
      </div>
      <div class=""actions"">
        ${u.isBanned
          ? `<button class=""btn"" id=""unbanBtn"">Unban</button>`
          : `<button class=""btn danger"" id=""banBtn"">Ban</button>`}
      </div>
      <div class=""msg"" id=""userActionMsg""></div>`;
    if (u.isBanned) {
      $(""#unbanBtn"").addEventListener(""click"", async () => {
        await adminFetch(""/admin/users/"" + encodeURIComponent(u.uid) + ""/unban"", { method: ""POST"" });
        lookupUser(u.uid); loadStats();
      });
    } else {
      $(""#banBtn"").addEventListener(""click"", async () => {
        const reason = prompt(""Ban reason (shown to the user):"", """") || null;
        await adminFetch(""/admin/users/"" + encodeURIComponent(u.uid) + ""/ban"", { method: ""POST"", body: JSON.stringify({ reason: reason }) });
        lookupUser(u.uid); loadStats();
      });
    }
  }
  $(""#userLookupBtn"").addEventListener(""click"", () => {
    const uid = $(""#userLookupUid"").value.trim();
    if (uid) lookupUser(uid);
  });
  $(""#userLookupUid"").addEventListener(""keydown"", (ev) => { if (ev.key === ""Enter"") $(""#userLookupBtn"").click(); });

  // ---- audit ----
  async function loadAudit() {
    const rows = await adminFetch(""/admin/audit?take=300"");
    $(""#auditBody"").innerHTML = (rows || []).map(r => `
      <tr>
        <td>${esc(fmtDate(r.createdAt))}</td>
        <td>${esc(r.actorUid)}</td>
        <td>${esc(r.action)}</td>
        <td>${esc(r.targetUid || r.targetGid || ""—"")}</td>
        <td>${esc(r.detail) || ""—""}</td>
      </tr>`).join("""");
  }
  $(""#auditRefresh"").addEventListener(""click"", loadAudit);

  // ---- boot ----
  if (token()) showApp(); else showLogin();
})();
</script>
</body>
</html>
";
}
