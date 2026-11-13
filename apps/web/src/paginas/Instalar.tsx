import { Fragment, useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { Cartao } from '../componentes/Base'
import { vozDisponivel } from '../componentes/LeitorVoz'

interface Diagnostico { [k: string]: string }

async function diagnosticar(): Promise<Diagnostico> {
  const d: Diagnostico = {}
  const nav = navigator as Navigator & { standalone?: boolean }
  d['Aberto como app instalado'] = window.matchMedia('(display-mode: standalone)').matches || nav.standalone ? 'sim' : 'não (navegador)'
  d['HTTPS'] = location.protocol === 'https:' || location.hostname === 'localhost' ? 'sim' : 'NÃO — instalação e GPS exigem HTTPS'
  try {
    const reg = await navigator.serviceWorker?.getRegistration()
    d['Service worker'] = reg?.active ? 'ativo' : 'não ativo (recarregue a página)'
  } catch { d['Service worker'] = 'indisponível' }
  d['Armazenamento local (IndexedDB)'] = 'indexedDB' in window ? 'disponível' : 'indisponível'
  try {
    const est = await navigator.storage?.estimate?.()
    if (est?.quota) d['Espaço reservado'] = `${Math.round((est.usage ?? 0) / 1024)} KB usados de ${Math.round(est.quota / 1024 / 1024)} MB`
    const pers = await navigator.storage?.persisted?.()
    d['Armazenamento persistente'] = pers ? 'sim' : 'não garantido pelo navegador'
  } catch { /* opcional */ }
  d['Notificações'] = 'Notification' in window ? Notification.permission : 'não suportadas neste navegador (no iPhone, só no app instalado — iOS 16.4+)'
  d['Push'] = 'PushManager' in window ? 'suportado' : 'não suportado'
  try {
    const g = await navigator.permissions?.query({ name: 'geolocation' as PermissionName })
    d['Localização (GPS)'] = g?.state ?? 'desconhecido'
  } catch { d['Localização (GPS)'] = 'consulte ao usar o mapa' }
  d['Leitura por voz'] = vozDisponivel() ? `disponível (${window.speechSynthesis.getVoices().filter((v) => v.lang.startsWith('pt')).length} voz(es) em português)` : 'indisponível'
  d['Conexão'] = navigator.onLine ? 'online' : 'offline'
  d['Navegador'] = navigator.userAgent
  d['Data/hora'] = new Date().toISOString()
  return d
}

export function Instalar() {
  const url = window.location.origin
  const [svg, setSvg] = useState('')
  const [diag, setDiag] = useState<Diagnostico | null>(null)
  const [copiado, setCopiado] = useState(false)
  const local = /^(localhost|127\.|10\.|192\.168\.)/.test(location.hostname)

  useEffect(() => {
    QRCode.toString(url, { type: 'svg', margin: 1, errorCorrectionLevel: 'M', color: { dark: '#193B44', light: '#FFFFFF' } }).then(setSvg).catch(() => setSvg(''))
    diagnosticar().then(setDiag)
  }, [url])

  const copiar = async () => {
    if (!diag) return
    const texto = Object.entries(diag).map(([k, v]) => `${k}: ${v}`).join('\n')
    try { await navigator.clipboard.writeText(texto); setCopiado(true) } catch { setCopiado(false) }
  }

  return (
    <div className="space-y-4">
      <p className="rounded-cartao bg-[#EDE9F3] p-3 text-[14px] text-[#4A3A6B]">
        <strong>Ambiente de demonstração.</strong> Centrais simuladas: nenhum órgão real é acionado. Em emergência real, ligue 193, 199, 192 ou 190.
      </p>
      <Cartao titulo="Instalar o Alerta em Ação no celular">
        <div className="grid gap-4 md:grid-cols-[220px_1fr] md:items-center">
          <div className="mx-auto w-[220px]">
            {svg ? <div role="img" aria-label={`QR Code para ${url}`} dangerouslySetInnerHTML={{ __html: svg }} /> : <p>Gerando QR Code…</p>}
          </div>
          <div>
            <p className="text-[15px]">Aponte a câmera do celular para o código ou abra:</p>
            <p className="mt-1 break-all text-[18px] font-bold text-oceano">{url}</p>
            {local && <p className="mt-2 rounded-controle bg-atencao-claro p-2 text-[13.5px] text-atencao">Este endereço é local e não abre em outro aparelho. Use a URL pública publicada (https://…onrender.com).</p>}
          </div>
        </div>
      </Cartao>

      <Cartao titulo="Android (Chrome)">
        <ol className="list-decimal space-y-1.5 pl-5 text-[15px]">
          <li>Abra o endereço acima no <strong>Chrome</strong>.</li>
          <li>Toque no menu <strong>⋮</strong> → <strong>Instalar app</strong> (ou “Adicionar à tela inicial”).</li>
          <li>Abra pelo ícone <strong>Alerta em Ação</strong> na tela inicial: ele abre em tela cheia, sem a barra do navegador.</li>
          <li>Permita <strong>Localização</strong> e <strong>Notificações</strong> quando solicitado.</li>
        </ol>
      </Cartao>

      <Cartao titulo="iPhone (Safari)">
        <ol className="list-decimal space-y-1.5 pl-5 text-[15px]">
          <li>Abra o endereço no <strong>Safari</strong> (outros navegadores no iPhone não instalam o app com notificações).</li>
          <li>Toque em <strong>Compartilhar</strong> (quadrado com seta) → <strong>Adicionar à Tela de Início</strong> → Adicionar.</li>
          <li>Abra <strong>pelo ícone</strong>. Notificações só funcionam no app instalado (iOS 16.4 ou superior), após tocar em “Ativar alertas”.</li>
          <li>Permita <strong>Localização</strong> quando solicitado. Sem ela, escolha o município manualmente.</li>
        </ol>
      </Cartao>

      <Cartao titulo="Permissões usadas">
        <ul className="list-disc space-y-1 pl-5 text-[15px]">
          <li><strong>Localização</strong> — somente com o app aberto e quando você autoriza; usada para identificar o município e no pedido de socorro.</li>
          <li><strong>Notificações</strong> — alertas para os locais que você escolheu monitorar. Sem internet não há como receber notificações.</li>
          <li><strong>Armazenamento</strong> — pedidos ficam salvos no aparelho até serem enviados.</li>
        </ul>
        <p className="mt-2 text-[13.5px] text-tinta-suave">A comunicação direta entre celulares sem internet (Nearby) é feita pelos <strong>apps nativos de demonstração</strong> (Android/iPhone), não por este app web.</p>
      </Cartao>

      <Cartao titulo="Diagnóstico deste aparelho" acao={<button type="button" onClick={copiar} className="min-h-11 rounded-controle border border-linha px-3 text-[14px] font-semibold">{copiado ? 'Copiado' : 'Copiar'}</button>}>
        <p className="mb-2 text-[13.5px] text-tinta-suave">Use nos testes físicos: copie e cole na matriz de testes (docs/MATRIZ_TESTES_DISPOSITIVOS.md).</p>
        {diag ? (
          <dl className="grid gap-x-3 gap-y-1 text-[14px] md:grid-cols-[230px_1fr]">
            {Object.entries(diag).map(([k, v]) => (<Fragment key={k}><dt className="font-semibold">{k}</dt><dd className="break-words text-tinta-suave">{v}</dd></Fragment>))}
          </dl>
        ) : <p>Verificando…</p>}
      </Cartao>
    </div>
  )
}
