import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { useAcessibilidade } from '../lib/acessibilidade'
import { deveAlarmar, pararAlarme, prepararAlarme, tocarAlarme } from '../lib/alarme'
import { useEvento } from '../lib/tempoReal'
import { IcAlerta } from './Icones'

interface Disparo { titulo: string; mensagem: string; url: string; familia: boolean }

/** Tela de alarme em primeiro plano: aparece por escrito, toca sirene (se ativada) e vibra. Nunca abre socorro sozinha. */
export function AlarmeEmergencia() {
  const { prefs } = useAcessibilidade()
  const [d, setD] = useState<Disparo | null>(null)
  const navegar = useNavigate()
  const botao = useRef<HTMLButtonElement>(null)
  const recentes = useRef(new Map<string, number>())
  useEffect(() => { prepararAlarme() }, [])

  const disparar = (n: Record<string, unknown>) => {
    const avisoId = (n.avisoId ?? n.AvisoId) as string | undefined
    const id = (n.id ?? n.Id) as string | undefined
    const dados = { tipo: n.tipo as string | undefined, evento: n.evento as string | undefined, nivel: n.nivel as string | undefined, avisoId }
    if (!deveAlarmar(dados)) return
    const url = (n.url as string | undefined) ?? (avisoId && id ? `/?notificacao=${id}` : '/alertas')
    const titulo = ((n.titulo ?? n.Titulo) as string) ?? 'Alerta'
    // O mesmo aviso pode chegar pelo tempo real e pelo push: toca uma vez só (janela de 2 min).
    const chave = `${url}|${titulo}`, agora = Date.now()
    if ((recentes.current.get(chave) ?? 0) > agora - 120_000) return
    recentes.current.set(chave, agora)
    setD({ titulo, mensagem: ((n.mensagem ?? n.Mensagem) as string) ?? '', url, familia: dados.tipo === 'familia' })
    tocarAlarme({ som: prefs.alarme })
  }
  useEvento('notificacao', (x) => disparar((x ?? {}) as Record<string, unknown>))
  // Push recebido com o app aberto (caso o tempo real tenha caído): o service worker repassa.
  useEffect(() => {
    if (!('serviceWorker' in navigator)) return
    const f = (e: MessageEvent) => { if (e.data?.tipo === 'alarme') disparar(e.data.dados ?? {}) }
    navigator.serviceWorker.addEventListener('message', f)
    return () => navigator.serviceWorker.removeEventListener('message', f)
  }) // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { if (d) botao.current?.focus() }, [d])
  useEffect(() => () => pararAlarme(), [])

  if (!d) return null
  const fechar = () => { pararAlarme(); setD(null) }
  return (
    <div className="fixed inset-0 z-[60] grid place-items-center bg-emergencia-escura/95 p-4" role="alertdialog" aria-modal="true" aria-labelledby="alarme-titulo" aria-describedby="alarme-texto">
      <div className="w-full max-w-md rounded-cartao bg-branco p-5 text-tinta shadow-xl">
        <div className="flex items-center gap-3 text-emergencia-escura">
          <IcAlerta tamanho={36} />
          <h2 id="alarme-titulo" className="text-[22px] font-bold leading-tight">{d.titulo}</h2>
        </div>
        <p id="alarme-texto" className="mt-3 text-[17px] leading-relaxed">{d.mensagem}</p>
        {d.familia && <p className="mt-2 text-[15px]">O pedido também segue para as equipes responsáveis. Se puder, ligue para a pessoa.</p>}
        <p className="mt-2 text-[14px] text-tinta-suave">Em perigo imediato, ligue 193 (Bombeiros) ou 199 (Defesa Civil).</p>
        <div className="mt-4 grid gap-2">
          <button ref={botao} className="min-h-14 rounded-controle bg-emergencia-escura px-4 text-[18px] font-bold text-branco" onClick={() => { fechar(); navegar(d.url) }}>Ver detalhes</button>
          <button className="min-h-12 rounded-controle bg-areia px-4 text-[16px] font-semibold text-petroleo ring-1 ring-linha" onClick={fechar}>Parar alarme</button>
          <a className="min-h-12 content-center rounded-controle text-center text-[16px] font-semibold text-emergencia-escura underline" href="tel:193" onClick={pararAlarme}>Ligar 193</a>
        </div>
      </div>
    </div>
  )
}
