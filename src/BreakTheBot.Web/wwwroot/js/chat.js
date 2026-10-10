(function () {
  const app = document.getElementById('chat-app');
  if (!app) return;

  const levelId = app.dataset.levelId;
  const token = app.dataset.token;
  const log = document.getElementById('chat-log');
  const input = document.getElementById('chat-input');
  const sendBtn = document.getElementById('chat-send');
  const quotaEl = document.getElementById('chat-quota');
  const defenseSwitch = document.getElementById('defense-switch'); // only exists after capture
  let busy = false;
  const tokenGoal = parseInt(app.dataset.tokenGoal || '0', 10);
  const rendersHtml = app.dataset.rendersHtml === 'true';

  // Level 6 only: builds the page that is shown inside the sandboxed iframe.
  // The CSP blocks every network request. The small shim turns alert/confirm/prompt into visible
  // notes, because a sandbox without "allow-modals" would hide them.
  function buildSandboxDoc(html) {
    const csp = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:";
    const shim =
      "<script>(function(){" +
      "function note(t){var d=document.createElement('div');" +
      "d.style.cssText='margin:6px 0;padding:6px 8px;border:1px solid #f85149;color:#f85149;font:12px monospace;background:rgba(248,81,73,.1)';" +
      "d.textContent=t;(document.body||document.documentElement).appendChild(d);}" +
      "window.alert=function(m){note('Script ran: alert('+m+')');};" +
      "window.confirm=window.prompt=function(m){note('Script ran: '+m);return null;};" +
      "})();<\/script>";
    const style = '<style>body{margin:8px;color:#e6edf3;background:#161b22;font:14px system-ui,sans-serif}</style>';
    return '<!doctype html><html><head><meta charset="utf-8">' +
      '<meta http-equiv="Content-Security-Policy" content="' + csp + '">' +
      style + shim + '</head><body>' + html + '</body></html>';
  }

  function addRenderedBubble(html) {
    const empty = document.getElementById('chat-empty');
    if (empty) empty.remove();

    const wrap = document.createElement('div');
    wrap.className = 'btb-msg btb-msg-bot btb-render-wrap';

    const label1 = document.createElement('div');
    label1.className = 'btb-render-label';
    label1.textContent = 'Rendered by the app (inside a sandbox):';

    // sandbox="allow-scripts" WITHOUT allow-same-origin: scripts can run, but they cannot reach
    // cookies, storage, or the parent page.
    const frame = document.createElement('iframe');
    frame.setAttribute('sandbox', 'allow-scripts');
    frame.className = 'btb-render-frame';
    frame.title = 'Sandboxed AI output';
    frame.srcdoc = buildSandboxDoc(html);   // set as a property, never string-concatenated into the page

    const label2 = document.createElement('div');
    label2.className = 'btb-render-label';
    label2.textContent = 'Raw output from the AI:';

    const raw = document.createElement('pre');
    raw.className = 'btb-render-raw';
    raw.textContent = html;                 // text only

    wrap.append(label1, frame, label2, raw);
    log.appendChild(wrap);
    scrollToBottom();
  }
  // Level 5 only: shows how many tokens the last reply used.
  function updateMeter(usage) {
    if (!tokenGoal || !usage) return;
    const out = usage.outputTokens || 0;
    const outEl = document.getElementById('token-out');
    const inEl = document.getElementById('token-in');
    const bar = document.getElementById('token-bar');
    if (outEl) outEl.textContent = out;
    if (inEl) inEl.textContent = usage.inputTokens || 0;
    if (bar) {
      bar.style.width = Math.min(100, Math.round(100 * out / tokenGoal)) + '%';
      bar.style.backgroundColor = out >= tokenGoal ? 'var(--btb-success)' : '';
    }
  }
  // ---------- helpers ----------

  function scrollToBottom() { log.scrollTop = log.scrollHeight; }

  // Every dynamic piece of text is set with textContent, never innerHTML.
  function addBubble(kind, text) {
    const empty = document.getElementById('chat-empty');
    if (empty) empty.remove();
    const div = document.createElement('div');
    div.className = 'btb-msg btb-msg-' + kind;
    div.textContent = text;
    log.appendChild(div);
    scrollToBottom();
    return div;
  }

  async function post(action, body) {
    const response = await fetch('/api/levels/' + levelId + '/' + action, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
      body: JSON.stringify(body || {})
    });
    const isJson = (response.headers.get('content-type') || '').includes('application/json');
    // A redirect to the login page means the session has expired.
    if (response.redirected || !isJson) {
      return { ok: false, status: response.status, data: { message: 'Your session has expired. Please log in again.' } };
    }
    return { ok: response.ok, status: response.status, data: await response.json() };
  }

  function errorText(res) {
    if (res.status === 429 && res.data.retryAfterSeconds) {
      return 'Slow down a little. Try again in ' + res.data.retryAfterSeconds + ' seconds.';
    }
    return res.data.message || 'Something went wrong. Please try again.';
  }

  function resultCard(kind, title, extra) {
    const card = document.createElement('div');
    card.className = 'btb-result btb-result-' + kind;
    const head = document.createElement('div');
    head.textContent = title;
    card.appendChild(head);
    (extra || []).filter(Boolean).forEach(function (text) {
      const q = document.createElement('div');
      q.className = 'btb-quote';
      q.textContent = text;
      card.appendChild(q);
    });
    return card;
  }

  // ---------- chat ----------

  function setBusy(value) {
    busy = value;
    sendBtn.disabled = value;
    input.disabled = value;
    if (!value) input.focus();
  }

  async function send() {
    const message = input.value.trim();
    if (!message || busy) return;

    addBubble('user', message);
    input.value = '';
    setBusy(true);
    const typing = addBubble('note', 'bot is thinking...');
    const defenseOn = !!(defenseSwitch && defenseSwitch.checked);

    try {
      const res = await post('chat', { message: message, defenseOn: defenseOn });
      typing.remove();

      if (!res.ok) { addBubble('error', errorText(res)); return; }

      const data = res.data;
      if (rendersHtml) addRenderedBubble(data.reply); else addBubble('bot', data.reply);
      if (data.systemNote) addBubble('note', data.systemNote);
      if (data.exploitDetected) {
        addBubble('note', defenseOn
          ? 'The defense was bypassed. Nice work!'
          : 'Exploit detected! Find the flag in the output and submit it on the left.');
      }
      if (data.quota && quotaEl) quotaEl.textContent = data.quota.userRemainingToday;
      updateMeter(data.usage);
    } catch (err) {
      typing.remove();
      addBubble('error', 'Could not reach the server. Check your connection and try again.');
    } finally {
      setBusy(false);
    }
  }

  sendBtn.addEventListener('click', send);
  input.addEventListener('keydown', function (e) {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send(); }
  });

  // ---------- reset ----------

  const resetBtn = document.getElementById('reset-btn');
  if (resetBtn) {
    resetBtn.addEventListener('click', async function () {
      if (!confirm('Clear this conversation? Your points and progress are kept.')) return;
      resetBtn.disabled = true;
      try {
        const res = await post('reset');
        if (res.ok) {
          log.replaceChildren();
          addBubble('note', 'Conversation cleared. Say hello to start again.');
        } else {
          addBubble('error', errorText(res));
        }
      } catch (err) {
        addBubble('error', 'Could not reach the server.');
      } finally {
        resetBtn.disabled = false;
      }
    });
  }

  // ---------- hints ----------

  const hintBtn = document.getElementById('hint-btn');
  if (hintBtn) {
    const hintList = document.getElementById('hint-list');
    const hintsLeftEl = document.getElementById('hints-left');
    const hintMsg = document.createElement('div');
    hintMsg.className = 'small mt-2';
    hintMsg.style.color = 'var(--btb-danger)';
    hintBtn.parentElement.appendChild(hintMsg);

    hintBtn.addEventListener('click', async function () {
      hintBtn.disabled = true;
      hintMsg.textContent = '';
      try {
        const res = await post('hint');
        if (!res.ok) {
          hintMsg.textContent = errorText(res);
          if (res.data.error !== 'hints_exhausted') hintBtn.disabled = false;
          return;
        }
        const li = document.createElement('li');
        li.className = 'mb-1';
        li.textContent = res.data.hint;
        hintList.appendChild(li);
        if (hintsLeftEl) hintsLeftEl.textContent = res.data.hintsRemaining;
        if (res.data.hintsRemaining > 0) {
          hintBtn.disabled = false;
        } else {
          hintBtn.textContent = 'No hints left';
        }
      } catch (err) {
        hintMsg.textContent = 'Could not reach the server.';
        hintBtn.disabled = false;
      }
    });
  }

  // ---------- flag submission ----------

  const flagBtn = document.getElementById('flag-btn');
  const flagInput = document.getElementById('flag-input');
  const flagMsg = document.getElementById('flag-msg');

  async function submitFlag() {
    const flag = flagInput.value.trim();
    if (!flag) { flagMsg.style.color = 'var(--btb-danger)'; flagMsg.textContent = 'Enter a flag first.'; return; }
        if (!/^FLAG\{.+\}$/.test(flag)) {
      flagMsg.style.color = 'var(--btb-danger)';
      flagMsg.textContent = 'A flag looks like FLAG{...}. Copy the whole thing, from FLAG{ to the closing }.';
      return;
    }
    flagBtn.disabled = true;
    flagMsg.textContent = '';
    try {
      const res = await post('flag', { flag: flag });
      if (!res.ok) {
        flagMsg.style.color = 'var(--btb-danger)';
        flagMsg.textContent = errorText(res);
      } else if (res.data.correct) {
        flagMsg.style.color = 'var(--btb-success)';
        flagMsg.textContent = 'Correct! +' + res.data.points + ' points. Loading the explanation...';
        setTimeout(function () { location.reload(); }, 1000);
        return;
      } else {
        flagMsg.style.color = 'var(--btb-danger)';
        flagMsg.textContent = 'That is not the right flag for your account. Keep trying.';
      }
    } catch (err) {
      flagMsg.style.color = 'var(--btb-danger)';
      flagMsg.textContent = 'Could not reach the server.';
    }
    flagBtn.disabled = false;
  }

  if (flagBtn && flagInput) {
    flagBtn.addEventListener('click', submitFlag);
    flagInput.addEventListener('keydown', function (e) {
      if (e.key === 'Enter') { e.preventDefault(); submitFlag(); }
    });
  }

  // ---------- replay (defense check) ----------

  const replayBtn = document.getElementById('replay-btn');
  const replayResult = document.getElementById('replay-result');
  if (replayBtn && replayResult) {
    replayBtn.addEventListener('click', async function () {
      replayBtn.disabled = true;
      replayResult.replaceChildren(resultCard('warn', 'Replaying your attack against the defense...'));
      try {
        const res = await post('replay');
        let card;
        if (!res.ok) {
          card = resultCard('bad', errorText(res));
        } else if (res.data.blocked) {
          card = resultCard('ok',
            'Blocked! The defense stopped your attack. You have ' + res.data.defensePoints + ' defense points on this level.',
            ['Bot reply: ' + res.data.reply, res.data.systemNote, 'Refresh the page to see your updated status.']);
        } else {
          card = resultCard('warn', 'Still vulnerable! Your attack got through.',
            [res.data.guidance, 'Bot reply: ' + res.data.reply]);
        }
        replayResult.replaceChildren(card);
      } catch (err) {
        replayResult.replaceChildren(resultCard('bad', 'Could not reach the server.'));
      } finally {
        replayBtn.disabled = false;
      }
    });
  }

  scrollToBottom();
})();