(function () {
  const app = document.getElementById('chat-app');
  if (!app) return;

  const levelId = app.dataset.levelId;
  const token = app.dataset.token;
  const log = document.getElementById('chat-log');
  const input = document.getElementById('chat-input');
  const sendBtn = document.getElementById('chat-send');
  const quotaEl = document.getElementById('chat-quota');
  let busy = false;

  function scrollToBottom() {
    log.scrollTop = log.scrollHeight;
  }

  // Adds a bubble. Text is always set with textContent (never innerHTML).
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

    try {
      const response = await fetch('/api/levels/' + levelId + '/chat', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'RequestVerificationToken': token
        },
        body: JSON.stringify({ message: message, defenseOn: false })
      });

      typing.remove();

      // A redirect to the login page means the session has expired.
      const isJson = (response.headers.get('content-type') || '').includes('application/json');
      if (response.redirected || !isJson) {
        addBubble('error', 'Your session has expired. Please log in again.');
        return;
      }

      const data = await response.json();

      if (!response.ok) {
        let text = data.message || 'Something went wrong. Please try again.';
        if (response.status === 429 && data.retryAfterSeconds) {
          text = 'Slow down a little. Try again in ' + data.retryAfterSeconds + ' seconds.';
        }
        addBubble('error', text);
        return;
      }

      addBubble('bot', data.reply);
      if (data.systemNote) addBubble('note', data.systemNote);
      if (data.quota && quotaEl) quotaEl.textContent = data.quota.userRemainingToday;
    } catch (err) {
      typing.remove();
      addBubble('error', 'Could not reach the server. Check your connection and try again.');
    } finally {
      setBusy(false);
    }
  }

  sendBtn.addEventListener('click', send);
  input.addEventListener('keydown', function (e) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      send();
    }
  });

  scrollToBottom();
})();