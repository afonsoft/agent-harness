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
    },

    // SPEC-20261001-ai-chat-openwebui: clipboard for message actions.
    copy: function (text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            return navigator.clipboard.writeText(text);
        }
        var area = document.createElement('textarea');
        area.value = text;
        document.body.appendChild(area);
        area.select();
        document.execCommand('copy');
        area.remove();
    }
};

// SPEC-20260930-mobile-responsive-ui FR-007: global keyboard shortcuts.
// Single document-level listener; all shortcuts are inert while focus is in
// an editable field (input/textarea/select/contenteditable, xterm, editors).
// A page exposes a shortcut target via data-shortcut-{command,search,new}
// attributes — no attribute, no shortcut, and the ? overlay only lists
// shortcuts with a live target on the current screen.
window.taskboardShortcuts = {
    _overlay: null,

    _isEditable: function (el) {
        return !!(el && el.closest && el.closest(
            'input, textarea, select, [contenteditable], .xterm-helper-textarea, .monaco-editor, .cm-editor'));
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

    handleKey: function (e) {
        if (e.key === 'Escape' && this._overlay) {
            this.hideHelp();
            e.preventDefault();
            return;
        }
        if (this._isEditable(e.target)) {
            return;
        }
        if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey &&
            (e.key === 'k' || e.key === 'K')) {
            var command = this._target('command') || this._target('search');
            if (command) {
                e.preventDefault();
                command.focus();
            }
            return;
        }
        if (e.ctrlKey || e.metaKey || e.altKey) {
            return;
        }
        if (e.key === '?') {
            e.preventDefault();
            this.toggleHelp();
            return;
        }
        if (e.key === 's' || e.key === 'n') {
            var name = e.key === 's' ? 'search' : 'new';
            var target = this._target(name);
            if (!target) {
                return;
            }
            e.preventDefault();
            if (name === 'search') {
                target.focus();
            } else {
                target.click();
            }
        }
    }
};

document.addEventListener('keydown', function (e) {
    window.taskboardShortcuts.handleKey(e);
});
