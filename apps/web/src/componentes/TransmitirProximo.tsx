import { useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { Botao } from './Base'
import { montarPedidoQr, urlAppNativo, urlRetransmissao, type PedidoQr } from '../lib/comunidade'

/**
 * Transmissão por aparelho próximo a partir da PWA.
 * Um navegador NÃO descobre aparelhos por Bluetooth/Wi-Fi Direct; por isso há dois caminhos reais:
 *  1) código QR lido pela câmera de um aparelho com internet (sem app instalado nele);
 *  2) abrir o pedido no app nativo Alerta em Ação Nearby, se instalado NESTE aparelho.
 */
export function TransmitirProximo({ ocorrenciaId, aoExibir }: { ocorrenciaId: string; aoExibir?: () => void }) {
  const [p, setP] = useState<PedidoQr | null>(null)
  const [svg, setSvg] = useState('')
  const [erro, setErro] = useState<string | null>(null)

  useEffect(() => {
    let vivo = true
    montarPedidoQr(ocorrenciaId).then(async (q) => {
      if (!vivo) return
      if (!q) { setErro('Este pedido não está salvo neste aparelho.'); return }
      setP(q)
      setSvg(await QRCode.toString(urlRetransmissao(q), { type: 'svg', margin: 2, errorCorrectionLevel: 'L', color: { dark: '#10282D', light: '#FFFFFF' } }))
      aoExibir?.()
    }).catch(() => setErro('Não foi possível gerar o código neste navegador.'))
    return () => { vivo = false }
  }, [ocorrenciaId, aoExibir])

  if (erro) return <p role="alert" className="text-[14.5px] text-emergencia-escura">{erro}</p>
  return (
    <div className="grid gap-3">
      <div>
        <h3 className="text-[17px] font-bold text-petroleo">Mostre este código para alguém por perto</h3>
        <p className="text-[14.5px]">A pessoa aponta a câmera do celular dela (com internet) e toca no link. O pedido vai para o servidor pela internet dela. Não precisa instalar nada.</p>
      </div>
      {svg ? <div role="img" aria-label="Código QR com o pedido de socorro" data-url={p ? urlRetransmissao(p) : undefined} className="mx-auto w-full max-w-[360px] rounded-cartao bg-branco p-1 ring-1 ring-linha" dangerouslySetInnerHTML={{ __html: svg }} /> : <p>Gerando código…</p>}
      <ul className="grid gap-1 text-[13.5px] text-tinta-suave">
        <li>O código leva: identificador do pedido, horário, {p?.lat != null ? `sua posição (±${p.prec ?? '?'} m)` : 'nenhuma posição (localização não informada)'}, situações marcadas, número de pessoas e necessidades.</li>
        <li>Não leva nome, telefone, relato escrito nem o identificador deste aparelho.</li>
        <li>Ler o código não confirma nada: o pedido só conta como transmitido quando o servidor confirmar. Se ninguém ler, ele continua salvo aqui e sai sozinho quando a internet voltar.</li>
      </ul>
      {p && (
        <details className="rounded-controle bg-fundo p-3 text-[14px]">
          <summary className="min-h-11 cursor-pointer font-semibold">Tenho o app Alerta em Ação Nearby neste aparelho</summary>
          <p className="mt-1">O app procura aparelhos próximos por Bluetooth e Wi-Fi (Android: Nearby Connections; iPhone: Multipeer Connectivity) e mostra cada etapa. O navegador sozinho não faz essa busca.</p>
          <Botao className="mt-2" variante="secundario" onClick={() => { location.href = urlAppNativo(p) }}>Abrir no app Nearby</Botao>
          <p className="mt-1 text-[12.5px] text-tinta-suave">Se nada acontecer, o app não está instalado. Use o código QR acima.</p>
        </details>
      )}
    </div>
  )
}

/** Etapas reais de entrega. Nenhuma etapa é marcada sem prova. */
export function EtapasEntrega({ salvoAqui, codigoExibido, canal, nivel }: { salvoAqui: boolean; codigoExibido: boolean; canal: string | null; nivel: number | null }) {
  const viaProximo = canal === 'ProximidadeQr' || canal === 'ProximidadeNearby'
  const etapas: { rotulo: string; estado: 'feito' | 'agora' | 'pendente' | 'nao_aplica'; detalhe?: string }[] = [
    { rotulo: 'Salvo neste celular', estado: salvoAqui || nivel ? 'feito' : 'pendente', detalhe: 'Nível 1' },
    ...(codigoExibido && !nivel ? [{ rotulo: 'Código mostrado para um celular próximo', estado: 'agora' as const, detalhe: 'aguardando leitura; ainda não é confirmação' }] : []),
    { rotulo: 'Recebido por um celular próximo e repassado', estado: viaProximo ? 'feito' : nivel ? 'nao_aplica' : 'pendente', detalhe: viaProximo ? (canal === 'ProximidadeQr' ? 'Níveis 2–3 · lido por QR' : 'Níveis 2–3 · Nearby') : nivel ? 'enviado direto pela internet' : 'Níveis 2–3' },
    { rotulo: 'Pedido de ajuda registrado', estado: nivel && nivel >= 4 ? 'feito' : 'pendente', detalhe: 'Nível 4' },
    { rotulo: 'A equipe de teste recebeu', estado: nivel === 5 ? 'feito' : 'pendente', detalhe: 'Nível 5 · demonstração, nenhum órgão real' },
  ]
  const marca = { feito: '✓', agora: '…', pendente: '○', nao_aplica: '–' }
  return (
    <ol className="grid gap-1.5" aria-label="Etapas de entrega do pedido">
      {etapas.map((e) => (
        <li key={e.rotulo} className="flex items-start gap-2 text-[14.5px]">
          <span aria-hidden="true" className={`mt-0.5 grid size-6 shrink-0 place-items-center rounded-full text-[13px] font-bold ${e.estado === 'feito' ? 'bg-seguro text-branco' : e.estado === 'agora' ? 'bg-atencao text-branco' : 'bg-fundo text-tinta-suave ring-1 ring-linha'}`}>{marca[e.estado]}</span>
          <span><span className={e.estado === 'feito' ? 'font-semibold' : ''}>{e.rotulo}</span>
            <span className="sr-only">: {e.estado === 'feito' ? 'concluído' : e.estado === 'agora' ? 'em andamento' : e.estado === 'nao_aplica' ? 'não se aplica' : 'pendente'}</span>
            {e.detalhe && <span className="block text-[12.5px] text-tinta-suave">{e.detalhe}</span>}</span>
        </li>
      ))}
    </ol>
  )
}
