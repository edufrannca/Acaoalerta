/**
 * O service worker (autoUpdate) instala a versão nova em segundo plano, mas a aba aberta continua com a antiga
 * até ser recarregada. Aqui: quando a versão nova assume, recarrega sozinho — exceto nas telas de pedido,
 * onde só avisa (o pedido está salvo no aparelho, mas não interrompemos quem está preenchendo).
 */
export const EVENTO_NOVA_VERSAO = 'alerta:nova-versao'

export function acompanharNovaVersao() {
  const sw = typeof navigator !== 'undefined' ? navigator.serviceWorker : undefined
  if (!sw) return
  const tinhaControlador = !!sw.controller // primeira visita: não recarrega
  let feito = false
  sw.addEventListener('controllerchange', () => {
    if (!tinhaControlador || feito) return
    feito = true
    if (/^\/(socorro|retransmitir)/.test(location.pathname)) window.dispatchEvent(new Event(EVENTO_NOVA_VERSAO))
    else location.reload()
  })
  // Procura versão nova ao voltar para o app e a cada 15 minutos.
  const verificar = () => { void sw.getRegistration().then((r) => r?.update()).catch(() => {}) }
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') verificar() })
  setInterval(verificar, 15 * 60_000)
}
