window.taskboard = {
    closeSidebar: function () {
        var el = document.getElementById('appSidebar');
        if (el && window.bootstrap?.Offcanvas) {
            var instance = window.bootstrap.Offcanvas.getInstance(el);
            if (instance) {
                instance.hide();
            }
        }
    },

    scrollToBottom: function (el) {
        if (el) {
            el.scrollTop = el.scrollHeight;
        }
    },

    // SPEC-20260922-ai-chat-command-bar RF-004: a modal must not gain
    // aria-hidden while a descendant still holds focus — blur first so
    // assistive tech never sees focus hidden inside the closing element.
    blurActiveElement: function () {
        var active = document.activeElement;
        if (active && typeof active.blur === 'function') {
            active.blur();
        }
    },

    focusElement: function (el) {
        if (el && typeof el.focus === 'function') {
            el.focus();
        }
    },

    // SPEC-20260918-sidebar-icon-rail RF-002: desktop icon-rail collapse.
    // The html[data-sidebar-collapsed] attribute drives all rail CSS; it is
    // updated even when localStorage is unavailable (private mode) so the
    // toggle still works for the session.
    getSidebarCollapsed: function () {
        try {
            return localStorage.getItem('harness.sidebar.collapsed') === 'true';
        } catch (e) {
            return false;
        }
    },

    setSidebarCollapsed: function (collapsed) {
        try {
            localStorage.setItem('harness.sidebar.collapsed', collapsed ? 'true' : 'false');
        } catch (e) { /* storage unavailable — session-only state */ }
        if (collapsed) {
            document.documentElement.dataset.sidebarCollapsed = 'true';
        } else {
            delete document.documentElement.dataset.sidebarCollapsed;
        }
    },

    // SPEC-20261008-locale-picker RF-001: persisted UI locale + html lang.
    getLocale: function () {
        try {
            return localStorage.getItem('harness.locale');
        } catch (e) {
            return null;
        }
    },

    setLocale: function (culture) {
        try {
            localStorage.setItem('harness.locale', culture);
        } catch (e) { /* storage unavailable — session-only state */ }
    },

    setHtmlLang: function (culture) {
        document.documentElement.lang = culture;
    },

    // SPEC-20260923-terminal-focus-mode RF-001/RF-002: CSS-only focus overlay —
    // the html[data-terminal-focus] attribute drives all focus-mode CSS (topbar
    // hidden, .terminal-page as a fixed viewport overlay). Session-only state:
    // nothing is persisted, and Terminal.razor clears it on dispose.
    setTerminalFocus: function (focused) {
        if (focused) {
            document.documentElement.dataset.terminalFocus = 'true';
        } else {
            delete document.documentElement.dataset.terminalFocus;
        }
    },

    // SPEC-20261001-terminal-memory-mobile RF-003: touch devices can pin the
    // virtual keybar without entering focus mode — html[data-terminal-keybar]
    // drives the CSS visibility rule alongside [data-terminal-focus].
    setTerminalKeybar: function (enabled) {
        if (enabled) {
            document.documentElement.dataset.terminalKeybar = 'true';
        } else {
            delete document.documentElement.dataset.terminalKeybar;
        }
    },

    // SPEC-20260920-global-repo-selector RF-001: the shared repo selection is
    // per-browser; storage failures degrade to session-only state.
    getSelectedRepo: function () {
        try {
            return localStorage.getItem('harness.selectedRepo');
        } catch (e) {
            return null;
        }
    },

    setSelectedRepo: function (repo) {
        try {
            if (repo) {
                localStorage.setItem('harness.selectedRepo', repo);
            } else {
                localStorage.removeItem('harness.selectedRepo');
            }
        } catch (e) { /* storage unavailable — session-only state */ }
    },

    // SPEC-20260928-ai-code-ux-simplify RF-001/RF-002: AI Code rail collapse
    // and the last-used agent CLI — per-browser, session-only fallback.
    getAiChatRailCollapsed: function () {
        try {
            return localStorage.getItem('harness.aichat.rail.collapsed') === 'true';
        } catch (e) {
            return false;
        }
    },

    setAiChatRailCollapsed: function (collapsed) {
        try {
            localStorage.setItem('harness.aichat.rail.collapsed', collapsed ? 'true' : 'false');
        } catch (e) { /* storage unavailable — session-only state */ }
    },

    getAiChatLastAgent: function () {
        try {
            return localStorage.getItem('harness.aichat.lastAgent');
        } catch (e) {
            return null;
        }
    },

    setAiChatLastAgent: function (agent) {
        try {
            if (agent) {
                localStorage.setItem('harness.aichat.lastAgent', agent);
            } else {
                localStorage.removeItem('harness.aichat.lastAgent');
            }
        } catch (e) { /* storage unavailable — session-only state */ }
    },

    // Devin-style mobile drawer: hide any open offcanvas after a nav pick
    // (backdrop tap already closes; navigation must too).
    closeMobileNav: function () {
        var el = document.querySelector('.offcanvas.show');
        if (el && window.bootstrap?.Offcanvas) {
            window.bootstrap.Offcanvas.getOrCreateInstance(el).hide();
        }
    }
};

// SPEC-20260923-cockpit-run-hardening RF-005: browser Notification API for
// approval gates — permission is requested lazily on the first cockpit visit;
// every failure path degrades to the in-app toast/modal silently.
window.taskboardNotify = {
    // SPEC-20261005 RF-008: current Notification.permission ('default',
    // 'granted', 'denied' ou 'unsupported').
    permission: function () {
        return ('Notification' in window) ? Notification.permission : 'unsupported';
    },

    // 'visible' | 'hidden' — browser notify só dispara com a aba fora de foco.
    isHidden: function () {
        return document.hidden === true;
    },

    // Per-browser override de notificações do chat (harness.chat.notify.*).
    // Lê 'true'/'false'; devolve null quando o key não existe ou storage está
    // indisponível — nesse caso o default global (config catalog) vale.
    getChatPref: function (name) {
        try {
            var v = localStorage.getItem('harness.chat.notify.' + name);
            return (v === 'true' || v === 'false') ? v : null;
        } catch (e) {
            return null;
        }
    },

    setChatPref: function (name, value) {
        try {
            if (value === null || value === undefined) {
                localStorage.removeItem('harness.chat.notify.' + name);
            } else {
                localStorage.setItem('harness.chat.notify.' + name, value ? 'true' : 'false');
            }
        } catch (e) { /* storage indisponível — default global segue */ }
    },

    // ---- SPEC-20261005 RF-009: Web Push (browser-fully-closed delivery) ----

    _vapidB64ToBytes: function (b64) {
        var pad = '='.repeat((4 - (b64.length % 4)) % 4);
        var raw = atob((b64 + pad).replace(/-/g, '+').replace(/_/g, '/'));
        var out = new Uint8Array(raw.length);
        for (var i = 0; i < raw.length; i++) {
            out[i] = raw.charCodeAt(i);
        }
        return out;
    },

    // Registers push-sw.js on demand and returns the registration (or null
    // when service workers are unsupported).
    _pushRegistration: async function () {
        if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
            return null;
        }
        try {
            return await navigator.serviceWorker.register('/push-sw.js');
        } catch (e) {
            return null;
        }
    },

    isPushSubscribed: async function () {
        try {
            if (!('serviceWorker' in navigator)) {
                return null;
            }
            var reg = await navigator.serviceWorker.getRegistration('/push-sw.js');
            var sub = reg ? await reg.pushManager.getSubscription() : null;
            return sub ? sub.endpoint : null;
        } catch (e) {
            return null;
        }
    },

    // Subscribes and stores the endpoint server-side. Returns
    // { subscribed: true, endpoint } | { error }.
    subscribePush: async function (vapidPublicKey, apiUrl) {
        try {
            var permission = await this.ensurePermission();
            if (permission !== 'granted') {
                return { error: 'permission ' + permission };
            }
            var reg = await this._pushRegistration();
            if (!reg) {
                return { error: 'service worker unsupported' };
            }
            var sub = await reg.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: this._vapidB64ToBytes(vapidPublicKey)
            });
            var json = sub.toJSON();
            json.userAgent = navigator.userAgent;
            var res = await fetch(apiUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                body: JSON.stringify(json)
            });
            if (!res.ok) {
                return { error: 'server rejected (' + res.status + ')' };
            }
            return { subscribed: true, endpoint: sub.endpoint };
        } catch (e) {
            return { error: String(e) };
        }
    },

    // Unsubscribes and removes the server row.
    unsubscribePush: async function (apiUrl) {
        try {
            if (!('serviceWorker' in navigator)) {
                return { unsubscribed: true };
            }
            var reg = await navigator.serviceWorker.getRegistration('/push-sw.js');
            var sub = reg ? await reg.pushManager.getSubscription() : null;
            if (sub) {
                await fetch(apiUrl + '?endpoint=' + encodeURIComponent(sub.endpoint), {
                    method: 'DELETE',
                    credentials: 'same-origin'
                });
                await sub.unsubscribe();
            }
            return { unsubscribed: true };
        } catch (e) {
            return { error: String(e) };
        }
    },

    ensurePermission: async function () {
        try {
            if (!('Notification' in window)) {
                return 'unsupported';
            }
            if (Notification.permission !== 'default') {
                return Notification.permission;
            }
            return await Notification.requestPermission();
        } catch (e) {
            // permission request can throw in embeds/unsupported contexts — treat as denied
            return 'denied';
        }
    },

    // Returns true when a notification was actually shown.
    notify: function (title, body, url, tag) {
        try {
            if (!('Notification' in window) || Notification.permission !== 'granted') {
                return false;
            }
            var n = new Notification(title, { body: body || '', tag: tag || 'harness-approval' });
            n.onclick = function () {
                try {
                    window.focus();
                    if (url) {
                        window.location.href = url;
                    }
                } catch (e) { /* navigation best-effort */ }
                n.close();
            };
            return true;
        } catch (e) {
            // notification failures degrade to the in-app toast — never propagate
            return false;
        }
    }
};

// SSE bridge (SPEC-20260918-ai-chat-threads): wraps EventSource and forwards
// named events to a .NET DotNetObjectReference. EventSource auto-reconnects;
// the server replays the backlog on reconnect, so consumers must dedupe.
window.taskboardSse = {
    _sources: {},

    // eventNames: array of SSE event names to forward (e.g. ['ai_chat.event', 'ai_chat.run'])
    connect: function (key, url, dotNetRef, eventNames) {
        this.disconnect(key);
        var source = new EventSource(url);
        (eventNames || []).forEach(function (name) {
            source.addEventListener(name, function (e) {
                dotNetRef.invokeMethodAsync('OnSseEvent', name, e.data);
            });
        });
        source.onerror = function () {
            dotNetRef.invokeMethodAsync('OnSseError');
        };
        this._sources[key] = source;
    },

    disconnect: function (key) {
        var source = this._sources[key];
        if (source) {
            source.close();
            delete this._sources[key];
        }
    },

    disconnectAll: function () {
        for (var key in this._sources) {
            if (Object.hasOwn(this._sources, key)) {
                this._sources[key].close();
            }
        }
        this._sources = {};
    }
};

// SPEC-20261001-chat-ux-compact FR-004: dynamic syntax colors for chat code
// blocks (markdown/bash/csharp/json/python/js…). Throttled — re-highlighting
// the whole container on every SSE delta would be O(n²).
window.taskboardChat = {
    _last: 0,
    _stick: {},

    // Auto-scroll: stick to the bottom while the user is at the bottom; a
    // manual scroll up detaches until they return (24px tolerance).
    _bindScroll: function (container) {
        if (container._chatScrollBound) {
            return;
        }
        container._chatScrollBound = true;
        this._stick[container.id] = true;
        var stick = this._stick;
        container.addEventListener('scroll', function () {
            // Scroll events are dispatched async — a programmatic scrollTop set
            // inside highlight() can be observed *after* a streamed chunk grew
            // scrollHeight, which would read as "not at bottom" and wrongly
            // detach stick. Skip events at (≈) the position we last set.
            var setTop = container._chatProgrammaticTop;
            if (setTop !== undefined && Math.abs(container.scrollTop - setTop) <= 1) {
                return;
            }
            stick[container.id] =
                container.scrollHeight - container.scrollTop - container.clientHeight < 24;
        });
    },

    highlight: function (containerId) {
        var container = document.getElementById(containerId);
        if (!container) {
            return;
        }
        this._bindScroll(container);
        var now = Date.now();
        if (window.hljs && now - this._last >= 400) {
            this._last = now;
            container.querySelectorAll('pre code:not(.hljs)').forEach(function (block) {
                try {
                    window.hljs.highlightElement(block);
                } catch (e) {
                    /* unknown language — leave plain */
                }
            });
        }
        if (this._stick[containerId]) {
            // Record the clamped target so the async scroll event can tell this
            // programmatic jump apart from a real user scroll.
            container._chatProgrammaticTop =
                Math.max(0, container.scrollHeight - container.clientHeight);
            container.scrollTop = container.scrollHeight;
        }
    },

    // SPEC-20261001-ai-chat-openwebui: clipboard for message actions.
    copy: function (text) {
        if (navigator.clipboard?.writeText) {
            return navigator.clipboard.writeText(text);
        }
        var area = document.createElement('textarea');
        area.value = text;
        document.body.appendChild(area);
        area.select();
        document.execCommand('copy'); // NOSONAR javascript:S1874 — único fallback fora de secure context
        area.remove();
    },

    // SPEC-20261005-chat-attachments-feedback RF-001: the paperclip proxies
    // a click into the hidden <input type=file> Blazor's InputFile renders.
    pickFile: function (inputId) {
        var el = document.getElementById(inputId);
        if (el) {
            el.click();
        }
    },

    // SPEC-20261014-chat-git-bar-overview RF-004: github.com/*/pull/N links in
    // assistant markdown get a hover card — fetched from the cached server
    // endpoint (60s), positioned near the link. Errors degrade to no card.
    bindPrCards: function (containerId) {
        var container = document.getElementById(containerId);
        if (!container || container._prCardsBound) {
            return;
        }
        container._prCardsBound = true;
        var card = null;
        var hideTimer = null;

        var hide = function () {
            if (card) {
                card.remove();
                card = null;
            }
        };
        var scheduleHide = function () {
            hideTimer = setTimeout(hide, 250);
        };
        var cancelHide = function () {
            if (hideTimer) {
                clearTimeout(hideTimer);
                hideTimer = null;
            }
        };

        container.addEventListener('mouseover', function (e) {
            var link = e.target.closest
                && e.target.closest('a[href*="github.com/"][href*="/pull/"]');
            if (!link) {
                return;
            }
            cancelHide();
            if (card && card._for === link.href) {
                return;
            }
            hide();
            card = document.createElement('div');
            card.className = 'chat-pr-card';
            card._for = link.href;
            card.textContent = '…';
            var rect = link.getBoundingClientRect();
            card.style.left = Math.max(8, Math.min(rect.left, window.innerWidth - 360)) + 'px';
            card.style.top = (rect.bottom + 6) + 'px';
            card.addEventListener('mouseenter', cancelHide);
            card.addEventListener('mouseleave', scheduleHide);
            document.body.appendChild(card);

            fetch('/api/local/chat/pr-card?url=' + encodeURIComponent(link.href),
                    { credentials: 'same-origin' })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (body) {
                    if (!card || card._for !== link.href || !body || !body.card) {
                        if (card && card._for === link.href) {
                            card.textContent = link.href;
                        }
                        return;
                    }
                    var c = body.card;
                    var state = c.merged ? 'merged' : c.state;
                    var checks = c.checksTotal > 0
                        ? 'checks ' + c.checksSucceeded + '/' + c.checksTotal
                            + (c.checksFailed > 0 ? ' (' + c.checksFailed + ' failed)' : '')
                        : 'no checks';
                    card.innerHTML = '';
                    var head = document.createElement('div');
                    var badge = document.createElement('span');
                    badge.className = 'pr-card-state ' + state;
                    badge.textContent = state + ' #' + c.number;
                    head.appendChild(badge);
                    var title = document.createElement('div');
                    title.className = 'pr-card-title';
                    title.textContent = c.title;
                    var meta = document.createElement('div');
                    meta.className = 'pr-card-meta';
                    meta.textContent = (c.author || 'unknown') + ' · ' + checks;
                    card.appendChild(head);
                    card.appendChild(title);
                    card.appendChild(meta);
                })
                .catch(function () {
                    if (card && card._for === link.href) {
                        card.textContent = link.href;
                    }
                });
        });
        container.addEventListener('mouseout', function (e) {
            var link = e.target.closest
                && e.target.closest('a[href*="github.com/"][href*="/pull/"]');
            if (link) {
                scheduleHide();
            }
        });
        container.addEventListener('scroll', hide);
    },

    // RF-003: clipboard files (screenshot paste etc.) flow into the same
    // hidden input — InputFile picks them up through the dispatched change.
    hookPaste: function (textareaId, fileInputId) {
        var area = document.getElementById(textareaId);
        var input = document.getElementById(fileInputId);
        if (!area || !input || area._attachPasteBound) {
            return;
        }
        area._attachPasteBound = true;
        area.addEventListener('paste', function (e) {
            var files = e.clipboardData && e.clipboardData.files;
            if (!files || files.length === 0) {
                return;
            }
            var dt = new DataTransfer();
            for (var i = 0; i < files.length; i++) {
                dt.items.add(files[i]);
            }
            input.files = dt.files;
            input.dispatchEvent(new Event('change', { bubbles: true }));
            e.preventDefault();
        });
    },

    // SPEC-20261005-chat-jobs-schedule-search RF-009: scroll a search-hit
    // message anchor (#m-{messageId}) into view inside the transcript.
    scrollIntoView: function (elementId) {
        var el = document.getElementById(elementId);
        if (el) {
            el.scrollIntoView({ block: 'center', behavior: 'smooth' });
        }
    },

    // SPEC-20261005-chat-plan-mode RF-004: export the reviewed plan as a
    // .md download (Blob URL — no server round-trip).
    download: function (filename, text, mime) {
        var blob = new Blob([text], { type: mime || 'text/markdown' });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(url);
    },

    // SPEC-20261011-chat-workspace-panel RF-001/RF-002: per-conversation pane
    // state — {open, tab, width} under harness.chat.workspace.<conversationId>.
    chatWsGet: function (conversationId) {
        try {
            var raw = localStorage.getItem('harness.chat.workspace.' + conversationId);
            return raw ? JSON.parse(raw) : null;
        } catch (e) {
            return null;
        }
    },

    chatWsSet: function (conversationId, state) {
        try {
            localStorage.setItem('harness.chat.workspace.' + conversationId, JSON.stringify(state));
        } catch (e) { /* storage indisponível — estado fica só na sessão */ }
    },

    // Generic localStorage helpers (SPEC-20261015 preview port memory).
    chatStoreGet: function (key) {
        try {
            return localStorage.getItem(key);
        } catch (e) {
            return null;
        }
    },

    chatStoreSet: function (key, value) {
        try {
            if (value === null || value === undefined) {
                localStorage.removeItem(key);
            } else {
                localStorage.setItem(key, value);
            }
        } catch (e) { /* sessão-only */ }
    },

    // RF-001: drag-resize 30–70% — pointermove aplica --ws-width direto no
    // painel (sem roundtrip .NET); no pointerup devolve o % final.
    initChatWsResize: function (handle, panel, dotNetRef) {
        if (!handle || !panel || handle._wsResizeBound) {
            return;
        }
        handle._wsResizeBound = true;
        var last = 45;
        var dragging = false;
        var onMove = function (e) {
            if (!dragging) {
                return;
            }
            var parent = panel.parentElement;
            if (!parent) {
                return;
            }
            var rect = parent.getBoundingClientRect();
            if (rect.width <= 0) {
                return;
            }
            var pct = ((rect.right - e.clientX) / rect.width) * 100;
            pct = Math.max(30, Math.min(70, pct));
            last = pct;
            panel.style.setProperty('--ws-width', pct.toFixed(2) + '%');
        };
        var onUp = function () {
            if (!dragging) {
                return;
            }
            dragging = false;
            document.body.classList.remove('chat-ws-dragging');
            document.removeEventListener('pointermove', onMove);
            document.removeEventListener('pointerup', onUp);
            dotNetRef.invokeMethodAsync('OnResizeEnd', last);
        };
        handle.addEventListener('pointerdown', function (e) {
            e.preventDefault();
            dragging = true;
            document.body.classList.add('chat-ws-dragging');
            document.addEventListener('pointermove', onMove);
            document.addEventListener('pointerup', onUp);
        });
    },

    // SPEC-20261015-chat-preview-panel RF-004: element picker — an overlay
    // inside the same-origin preview iframe outlines the hovered element;
    // clicking captures {selector, tag, text, pageUrl} and postMessages the
    // parent, which turns it into a composer quote chip.
    previewPickerEnable: function (iframe) {
        var doc = iframe && iframe.contentDocument;
        if (!doc || iframe._pickerOn) {
            return;
        }
        iframe._pickerOn = true;
        var box = doc.createElement('div');
        box.id = '__pick';
        box.style.cssText = 'position:fixed;pointer-events:none;z-index:2147483647;'
            + 'border:2px solid #7c3aed;background:rgba(124,58,237,.12);display:none;';
        doc.body.appendChild(box);

        var onMove = function (e) {
            var el = doc.elementFromPoint(e.clientX, e.clientY);
            if (!el || el === box) {
                box.style.display = 'none';
                return;
            }
            var r = el.getBoundingClientRect();
            box.style.display = 'block';
            box.style.left = r.left + 'px';
            box.style.top = r.top + 'px';
            box.style.width = r.width + 'px';
            box.style.height = r.height + 'px';
            box._el = el;
        };
        var onClick = function (e) {
            var el = box._el || doc.elementFromPoint(e.clientX, e.clientY);
            if (!el || el === box) {
                return;
            }
            e.preventDefault();
            e.stopPropagation();
            window.parent.postMessage({
                type: 'harness-preview-pick',
                selector: previewPickSelector(el),
                tag: el.tagName.toLowerCase(),
                text: (el.textContent || '').trim().slice(0, 120),
                pageUrl: doc.location ? doc.location.href : ''
            }, window.location.origin);
        };
        doc.addEventListener('mousemove', onMove, true);
        doc.addEventListener('click', onClick, true);
        iframe._pickerTeardown = function () {
            doc.removeEventListener('mousemove', onMove, true);
            doc.removeEventListener('click', onClick, true);
            box.remove();
            iframe._pickerOn = false;
        };
    },

    previewPickerDisable: function (iframe) {
        if (iframe && iframe._pickerTeardown) {
            iframe._pickerTeardown();
            iframe._pickerTeardown = null;
        }
    },

    // Parent-side listener — latest registered ref wins (tab recreations).
    previewPickerListen: function (dotNetRef) {
        window._previewPickRef = dotNetRef;
        if (window._previewPickBound) {
            return;
        }
        window._previewPickBound = true;
        window.addEventListener('message', function (e) {
            if (e.origin !== window.location.origin || !e.data
                || e.data.type !== 'harness-preview-pick' || !window._previewPickRef) {
                return;
            }
            window._previewPickRef.invokeMethodAsync('OnPreviewPick', e.data);
        });
    },

    // ---- SPEC-20261017-chat-polish: chat hotkeys (RF-003/RF-006) ----

    // Ctrl+K palette, Ctrl+. pause-or-stop, Ctrl+Shift+E mark-done. Escape is
    // NOT handled here — Blazor closes open surfaces topmost-first; a page
    // with nothing open forwards Escape via 'escape'.
    bindChatHotkeys: function (dotNetRef) {
        this.unbindChatHotkeys();
        var handler = function (e) {
            var mod = e.ctrlKey || e.metaKey;
            if (mod && !e.altKey && !e.shiftKey && (e.key === 'k' || e.key === 'K')) {
                e.preventDefault();
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('OnChatHotkey', 'palette');
                return;
            }
            if (mod && !e.altKey && !e.shiftKey && e.key === '.') {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnChatHotkey', 'pause');
                return;
            }
            if (mod && e.shiftKey && !e.altKey && (e.key === 'e' || e.key === 'E')) {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('OnChatHotkey', 'done');
            }
        };
        // capture=true beats taskboardShortcuts' Ctrl+K composer-focus on this page.
        document.addEventListener('keydown', handler, true);
        this._chatHotkeyHandler = handler;
    },

    unbindChatHotkeys: function () {
        if (this._chatHotkeyHandler) {
            document.removeEventListener('keydown', this._chatHotkeyHandler, true);
            this._chatHotkeyHandler = null;
        }
    },

    // ---- SPEC-20261017-chat-polish RF-005: voice input (Web Speech API) ----

    _recognition: null,

    voiceSupported: function () {
        return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
    },

    // Streams interim+final transcripts into OnVoiceTranscript; the host
    // decides what lands in the composer (never auto-send per RF-005).
    voiceStart: function (dotNetRef, lang) {
        var Ctor = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!Ctor) {
            return false;
        }
        this.voiceStop();
        var rec = new Ctor();
        rec.lang = lang || 'pt-BR';
        rec.continuous = true;
        rec.interimResults = true;
        var self = this;
        rec.onresult = function (e) {
            var text = '';
            for (var i = 0; i < e.results.length; i++) {
                text += e.results[i][0].transcript;
            }
            dotNetRef.invokeMethodAsync('OnVoiceTranscript', text);
        };
        rec.onerror = function () { self.voiceStop(); };
        rec.onend = function () { dotNetRef.invokeMethodAsync('OnVoiceEnd', ''); };
        this._recognition = rec;
        try {
            rec.start();
            return true;
        } catch (e) {
            this._recognition = null;
            return false;
        }
    },

    voiceStop: function () {
        if (this._recognition) {
            try { this._recognition.stop(); } catch (e) { /* already stopped */ }
            this._recognition = null;
        }
    }
};

// RF-004: CSS selector for the picked element — id shortcut, else a short
// tag.class chain (≤4 ancestors) with :nth-of-type when siblings repeat.
function previewPickSelector(el) {
    if (el.id) {
        return '#' + CSS.escape(el.id);
    }
    var parts = [];
    var cur = el;
    while (cur && cur.nodeType === 1 && parts.length < 4) {
        var seg = cur.tagName.toLowerCase();
        var cls = typeof cur.className === 'string'
            ? cur.className.trim().split(/\s+/).filter(Boolean).slice(0, 2)
            : [];
        if (cls.length) {
            seg += '.' + cls.map(function (c) { return CSS.escape(c); }).join('.');
        }
        var parent = cur.parentElement;
        if (parent) {
            var same = Array.prototype.filter.call(
                parent.children, function (c) { return c.tagName === cur.tagName; });
            if (same.length > 1) {
                seg += ':nth-of-type(' + (same.indexOf(cur) + 1) + ')';
            }
        }
        parts.unshift(seg);
        if (cur.id) {
            parts.unshift('#' + CSS.escape(cur.id));
            break;
        }
        cur = parent;
    }
    return parts.join(' > ');
}

// SPEC-20260930-mobile-responsive-ui FR-007: global keyboard shortcuts.
// Single document-level listener; all shortcuts are inert while focus is in
// an editable field (input/textarea/select/contenteditable, xterm, editors).
// A page exposes a shortcut target via data-shortcut-{command,search,new}
// attributes — no attribute, no shortcut, and the ? overlay only lists
// shortcuts with a live target on the current screen.
window.taskboardShortcuts = {
    _overlay: null,

    _isEditable: function (el) {
        return !!el?.closest?.(
            'input, textarea, select, [contenteditable], .xterm-helper-textarea, .monaco-editor, .cm-editor');
    },

    _target: function (name) {
        return document.querySelector('[data-shortcut-' + name + ']');
    },

    _entries: function () {
        var entries = [
            { keys: '?', label: 'Mostrar/ocultar ajuda de atalhos' },
            { keys: 'Esc', label: 'Fechar overlay' }
        ];
        if (this._target('command')) {
            entries.push({ keys: 'Ctrl+K', label: 'Focar comando/composer da página' });
        }
        if (this._target('search')) {
            entries.push({ keys: 's', label: 'Focar a busca da página' });
        }
        if (this._target('new')) {
            entries.push({ keys: 'n', label: 'Ação primária (novo)' });
        }
        return entries;
    },

    _buildOverlay: function () {
        var overlay = document.createElement('div');
        overlay.id = 'shortcut-help-overlay';
        overlay.className = 'shortcut-help-overlay';
        overlay.setAttribute('role', 'dialog');
        overlay.setAttribute('aria-label', 'Atalhos de teclado');
        var card = document.createElement('div');
        card.className = 'shortcut-help-card';
        var title = document.createElement('h2');
        title.className = 'shortcut-help-title';
        title.textContent = 'Atalhos de teclado';
        card.appendChild(title);
        var list = document.createElement('ul');
        list.className = 'shortcut-help-list';
        this._entries().forEach(function (entry) {
            var item = document.createElement('li');
            var kbd = document.createElement('kbd');
            kbd.textContent = entry.keys;
            item.appendChild(kbd);
            item.appendChild(document.createTextNode(' ' + entry.label));
            list.appendChild(item);
        });
        card.appendChild(list);
        overlay.appendChild(card);
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) {
                window.taskboardShortcuts.hideHelp();
            }
        });
        return overlay;
    },

    showHelp: function () {
        if (!this._overlay) {
            this._overlay = this._buildOverlay();
            document.body.appendChild(this._overlay);
        }
    },

    hideHelp: function () {
        if (this._overlay) {
            this._overlay.remove();
            this._overlay = null;
        }
    },

    toggleHelp: function () {
        if (this._overlay) {
            this.hideHelp();
        } else {
            this.showHelp();
        }
    },

    _handleCommandKey: function (e) {
        if (!(e.ctrlKey || e.metaKey) || e.altKey || e.shiftKey ||
            (e.key !== 'k' && e.key !== 'K')) {
            return false;
        }
        var command = this._target('command') || this._target('search');
        if (!command) {
            return false;
        }
        e.preventDefault();
        command.focus();
        return true;
    },

    _handlePlainKey: function (e) {
        if (e.key === '?') {
            e.preventDefault();
            this.toggleHelp();
            return;
        }
        if (e.key !== 's' && e.key !== 'n') {
            return;
        }
        var target = this._target(e.key === 's' ? 'search' : 'new');
        if (!target) {
            return;
        }
        e.preventDefault();
        if (e.key === 's') {
            target.focus();
        } else {
            target.click();
        }
    },

    handleKey: function (e) {
        if (e.key === 'Escape' && this._overlay) {
            e.preventDefault();
            this.hideHelp();
            return;
        }
        if (this._isEditable(e.target)) {
            return;
        }
        if (this._handleCommandKey(e)) {
            return;
        }
        if (e.ctrlKey || e.metaKey || e.altKey) {
            return;
        }
        this._handlePlainKey(e);
    }
};

document.addEventListener('keydown', function (e) {
    window.taskboardShortcuts.handleKey(e);
});
