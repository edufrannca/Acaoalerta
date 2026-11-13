import { useId, useSyncExternalStore } from 'react'
import { useAcessibilidade } from '../lib/acessibilidade'
import { assinar, continuar, ehIOS, falar, obter, parar, pausar } from '../lib/fala'

/** Há algum caminho de voz: síntese do aparelho ou áudio da voz do servidor. */
export function vozDisponivel() {
  return typeof window !== 'undefined' && (('speechSynthesis' in window && 'SpeechSynthesisUtterance' in window) || typeof Audio !== 'undefined')
}

function IconeSom() {
  return <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M11 5 6 9H3v6h3l5 4z" /><path d="M15.5 8.5a5 5 0 0 1 0 7M18.5 5.5a9 9 0 0 1 0 13" /></svg>
}

export function LeitorVoz({ texto, rotulo = 'Ouvir', compacto = false, destaque = false }: { texto: string; rotulo?: string; compacto?: boolean; destaque?: boolean }) {
  const id = useId()
  const { prefs, alterar } = useAcessibilidade()
  const s = useSyncExternalStore(assinar, obter, obter)
  const meu = s.dono === id
  const estado = meu ? s.estado : 'parado'

  if (!vozDisponivel()) return <p className="text-[13px] text-tinta-suave">Leitura por voz indisponível neste navegador.</p>

  const ouvir = () => falar(id, texto, prefs.velocidadeVoz, prefs.voz)
  const base = 'inline-flex min-h-11 items-center justify-center gap-2 rounded-controle px-3 py-2 text-[14.5px] font-semibold'
  const sec = `${base} bg-branco text-petroleo ring-1 ring-linha hover:bg-areia`
  return (
    <div className={`${compacto ? '' : 'mt-3'} grid gap-2`} role="group" aria-label={`Leitura por voz: ${rotulo}`}>
      <div className="flex flex-wrap items-center gap-2">
        {(estado === 'parado' || estado === 'erro') && (
          <button type="button" onClick={ouvir} className={destaque ? `${base} w-full bg-areia-escura text-petroleo hover:bg-linha/60` : sec}>
            <IconeSom />{estado === 'erro' ? 'Tentar novamente' : rotulo}
          </button>
        )}
        {estado === 'preparando' && <span className={`${sec} opacity-80`} role="status">Preparando áudio…</span>}
        {estado === 'falando' && <button type="button" className={sec} onClick={pausar}>Pausar</button>}
        {estado === 'pausado' && <button type="button" className={sec} onClick={continuar}>Continuar</button>}
        {estado !== 'parado' && estado !== 'erro' && <button type="button" className={sec} onClick={parar}>Parar</button>}
        {estado !== 'parado' && estado !== 'erro' && <button type="button" className={sec} onClick={ouvir}>Repetir</button>}
        {!compacto && (
          <label className="flex items-center gap-1.5 text-[13px] text-tinta-suave">
            Velocidade
            <select className="min-h-11 rounded-controle border border-linha bg-branco px-2 text-[14px]" value={prefs.velocidadeVoz}
              onChange={(e) => alterar({ velocidadeVoz: Number(e.target.value) })}>
              <option value={0.75}>Lenta</option><option value={1}>Normal</option><option value={1.25}>Rápida</option>
            </select>
          </label>
        )}
      </div>
      {meu && s.estado === 'falando' && s.modo === 'servidor' && <p className="text-[12.5px] text-tinta-suave">Voz do servidor do Alerta em Ação (sintetizada sem serviços externos).</p>}
      {meu && s.aviso && <p className="text-[12.5px] text-atencao" role="status">{s.aviso}</p>}
      {meu && s.estado === 'erro' && (
        <p className="text-[13px] text-emergencia-escura" role="alert">
          {s.erro}{ehIOS() ? ' No iPhone, confira o volume e a chave lateral de silencioso.' : ''}
        </p>
      )}
    </div>
  )
}
