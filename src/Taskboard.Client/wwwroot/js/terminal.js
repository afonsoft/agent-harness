// xterm.js wrapper for the /terminal page (SPEC-20260917-cli-agents-terminal).
// Rendering/keystrokes only — SignalR transport lives in C# (HubConnection).
window.taskboardTerminal = (() => {
    let term = null;
    let fit = null;
    let dotNet = null;
    let observer = null;

    function init(elementId, dotNetRef) {
        const el = document.getElementById(elementId);
        if (!el || typeof Terminal === 'undefined') {
            return false;
        }

        term = new Terminal({
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
        fit = new FitAddon.FitAddon();
        term.loadAddon(fit);
        term.open(el);
        fit.fit();

        dotNet = dotNetRef;
        term.onData(d => dotNet.invokeMethodAsync('OnTerminalData', d));
        observer = new ResizeObserver(() => {
            try {
                fit.fit();
                dotNet.invokeMethodAsync('OnTerminalResize', term.cols, term.rows);
            } catch { /* element gone */ }
        });
        observer.observe(el);
        return true;
    }

    function write(data) {
        if (term) {
            term.write(data);
        }
    }

    function focus() {
        if (term) {
            term.focus();
        }
    }

    function dispose() {
        if (observer) {
            observer.disconnect();
            observer = null;
        }
        if (term) {
            term.dispose();
            term = null;
        }
        dotNet = null;
    }

    return { init, write, focus, dispose };
})();
