/* Tratamento de Web Push no service worker (importado pelo SW gerado pelo Workbox). */
// Emergências: pedido de socorro em local acompanhado, aviso oficial e alagamento elevado/crítico.
function emergencia(tag) {
  return /^familia-.*:pedido$/.test(tag) || /^aviso-/.test(tag) || /^preventivo-.*:(Elevado|Critico)$/.test(tag)
}
self.addEventListener('push', (event) => {
  let d = { titulo: 'Alerta em Ação', corpo: 'Há um novo aviso para a sua região.', url: '/', tag: 'aviso' }
  try { if (event.data) d = { ...d, ...event.data.json() } } catch (e) { /* conteúdo inválido */ }
  const urgente = emergencia(d.tag || '')
  event.waitUntil(Promise.all([
    self.registration.showNotification(urgente ? '🚨 ' + d.titulo : d.titulo, {
      body: d.corpo, tag: d.tag, icon: '/icone-192.png', badge: '/icone-192.png', data: { url: d.url }, requireInteraction: true,
      renotify: true, silent: false, vibrate: urgente ? [800, 300, 800, 300, 800, 300, 800] : [300, 100, 300],
    }),
    // Se o app estiver aberto, ele toca o alarme e mostra a tela de emergência.
    urgente ? self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((lista) => {
      const tipo = d.tag.startsWith('familia-') ? 'familia' : d.tag.startsWith('preventivo-') ? 'preventivo' : undefined
      const nivel = tipo === 'preventivo' ? d.tag.split(':').pop() : undefined
      const dados = { tipo, evento: tipo === 'familia' ? 'pedido' : undefined, nivel, avisoId: tipo ? undefined : d.tag.slice(6), titulo: d.titulo, mensagem: d.corpo, url: d.url }
      for (const c of lista) c.postMessage({ tipo: 'alarme', dados })
    }) : Promise.resolve(),
  ]))
})
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  const url = (event.notification.data && event.notification.data.url) || '/'
  event.waitUntil(self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((lista) => {
    for (const c of lista) { if ('focus' in c) { c.navigate(url); return c.focus() } }
    return self.clients.openWindow(url)
  }))
})
