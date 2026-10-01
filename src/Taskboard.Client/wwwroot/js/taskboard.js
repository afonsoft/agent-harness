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
    }
};

// SPEC-20260923-cockpit-run-hardening RF-005: browser Notification API for
// approval gates — permission is requested lazily on the first cockpit visit;
// every failure path degrades to the in-app toast/modal silently.
window.taskboardNotify = {
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
            return 'denied';
        }
    },

    // Returns true when a notification was actually shown.
    notify: function (title, body, url) {
        try {
            if (!('Notification' in window) || Notification.permission !== 'granted') {
                return false;
            }
            var n = new Notification(title, { body: body || '', tag: 'harness-approval' });
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

    highlight: function (containerId) {
        var container = document.getElementById(containerId);
        if (!container || !window.hljs) {
            return;
        }
        var now = Date.now();
        if (now - this._last < 400) {
            return;
        }
        this._last = now;
        container.querySelectorAll('pre code:not(.hljs)').forEach(function (block) {
            try {
                window.hljs.highlightElement(block);
            } catch (e) {
                /* unknown language — leave plain */
            }
        });
    }
};
