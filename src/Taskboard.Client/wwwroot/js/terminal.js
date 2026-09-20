// xterm.js wrapper for the /terminal page — multiple tabbed instances keyed by
// elementId (SPEC-20260917-terminal-tabs). Rendering/keystrokes only; SignalR
// transport lives in C# (HubConnection).
window.taskboardTerminal = (() => {
    const terms = new Map(); // elementId -> { term, fit, observer, dotNet, tabKey }

    function copySelection(term) {
        const text = term.getSelection();
        if (!text) {
            return;
        }
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).catch(() => { /* permission denied */ });
        }
    }

    function pasteClipboard(term) {
        if (navigator.clipboard && navigator.clipboard.readText) {
            navigator.clipboard.readText()
                .then(t => { if (t) { term.paste(t); } })
                .catch(() => { /* permission denied */ });
            return true;
        }
        // Clipboard API unavailable (non-secure context): don't swallow the key.
        term.writeln('\r\n\x1b[33m[paste indisponível neste contexto — use o menu do browser]\x1b[0m');
        return false;
    }

    function attachKeys(term) {
        term.attachCustomKeyEventHandler(ev => {
            if (ev.type !== 'keydown') {
                return true;
            }

            const ctrl = ev.ctrlKey && !ev.altKey && !ev.metaKey;
            const key = ev.key.toLowerCase();

            if (ctrl && key === 'c') {
                // Ctrl+C: copy when there is a selection, otherwise let \x03 through.
                if (term.hasSelection()) {
                    copySelection(term);
                    return false;
                }
                return !ev.shiftKey; // Ctrl+Shift+C without selection: swallow
            }

            if ((ctrl && key === 'v') || (ev.shiftKey && ev.key === 'Insert' && !ctrl)) {
                return !pasteClipboard(term);
            }

            return true;
        });
    }

    function init(elementId, dotNetRef, tabKey) {
        const el = document.getElementById(elementId);
        if (!el || typeof Terminal === 'undefined') {
            return false;
        }

        const term = new Terminal({
            cursorBlink: true,
            fontSize: 13,
            fontFamily: 'ui-monospace, SFMono-Regular, Menlo, Consolas, monospace',
            scrollback: 5000,
            theme: {
                background: '#0d1117',
                foreground: '#e6edf3',
                cursor: '#58a6ff'
            }
        });
        const fit = new FitAddon.FitAddon();
        term.loadAddon(fit);
        term.open(el);
        fit.fit();
        attachKeys(term);

        const entry = { term, fit, observer: null, dotNet: dotNetRef, tabKey, el, lastCols: 0, lastRows: 0 };
        term.onData(d => dotNetRef.invokeMethodAsync('OnTerminalData', tabKey, d));
        entry.observer = new ResizeObserver(() => {
            try {
                reportResize(entry);
            } catch { /* element gone */ }
        });
        entry.observer.observe(el);
        terms.set(elementId, entry);
        reportResize(entry);
        return true;
    }

    // Fits the addon and reports the new size — only when the element is
    // visible, non-degenerate, and the size actually changed. Hidden panes
    // (d-none) and mid-layout transitions produce garbage dims (e.g. rows 3)
    // that must never reach the server.
    function reportResize(entry) {
        const el = entry.el;
        if (!el || el.offsetParent === null || el.clientHeight === 0 || el.clientWidth === 0) {
            return;
        }
        entry.fit.fit();
        const { cols, rows } = entry.term;
        if (cols < 2 || rows < 2 || (cols === entry.lastCols && rows === entry.lastRows)) {
            return;
        }
        entry.lastCols = cols;
        entry.lastRows = rows;
        entry.dotNet.invokeMethodAsync('OnTerminalResize', entry.tabKey, cols, rows);
    }

    function write(elementId, data) {
        const e = terms.get(elementId);
        if (e) {
            e.term.write(data);
        }
    }

    function focus(elementId) {
        const e = terms.get(elementId);
        if (e) {
            e.term.focus();
        }
    }

    function fitNow(elementId) {
        const e = terms.get(elementId);
        if (!e) {
            return;
        }
        try {
            reportResize(e);
        } catch { /* element gone */ }
    }

    function reset(elementId) {
        const e = terms.get(elementId);
        if (e) {
            e.term.reset();
        }
    }

    function dispose(elementId) {
        const e = terms.get(elementId);
        if (!e) {
            return;
        }
        if (e.observer) {
            e.observer.disconnect();
        }
        e.term.dispose();
        terms.delete(elementId);
    }

    function disposeAll() {
        for (const id of [...terms.keys()]) {
            dispose(id);
        }
    }

    return { init, write, focus, fitNow, reset, dispose, disposeAll };
})();
