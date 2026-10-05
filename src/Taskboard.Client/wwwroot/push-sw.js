// SPEC-20261005-chat-background-resume RF-009: Web Push service worker —
// delivers run.completed notifications even with every tab closed. Registered
// lazily by taskboardNotify.subscribePush (only when the user opts in).
'use strict';

self.addEventListener('push', function (event) {
    if (!event.data) {
        return;
    }

    var data;
    try {
        data = event.data.json();
    } catch (e) {
        data = { title: '', status: 'completed' };
    }

    var title = (data.title && data.title.length > 0) ? data.title : 'Conversa';
    var body = data.status === 'completed' ? 'Resposta concluída'
        : data.status === 'stopped' ? 'Resposta interrompida'
        : data.status === 'interrupted' ? 'Interrompida — abra para retomar'
        : 'Falhou' + (data.error ? ' — ' + data.error : '');

    event.waitUntil(
        self.registration.showNotification('Harness — ' + title, {
            body: body,
            tag: 'harness-chat-' + (data.runId || ''),
            data: { url: data.url || '/ai-chat' }
        })
    );
});

self.addEventListener('notificationclick', function (event) {
    event.notification.close();
    var url = (event.notification.data && event.notification.data.url) || '/ai-chat';
    event.waitUntil(
        clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (clientList) {
            // Reuse an open Harness tab when one exists — else open a new one.
            for (var i = 0; i < clientList.length; i++) {
                var client = clientList[i];
                if ('focus' in client) {
                    client.focus();
                    client.navigate(url);
                    return;
                }
            }
            return clients.openWindow(url);
        })
    );
});
