/* Tratamento de Web Push no service worker (importado pelo SW gerado pelo Workbox). */
self.addEventListener('push', (event) => {
  let d = { titulo: 'Alerta em Ação', corpo: 'Há um novo aviso para a sua região.', url: '/', tag: 'aviso' }
  try { if (event.data) d = { ...d, ...event.data.json() } } catch (e) { /* conteúdo inválido */ }
  event.waitUntil(self.registration.showNotification(d.titulo, {
    body: d.corpo, tag: d.tag, icon: '/icone-192.png', badge: '/icone-192.png', data: { url: d.url }, requireInteraction: true,
  }))
})
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const url = (event.notification.data && event.notification.data.url) || '/'
  event.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((lista) => {
    for (const c of lista) { if ('focus' in c) { c.navigate(url); return c.focus() } }
    return self.clients.openWindow(url)
  }))
})
