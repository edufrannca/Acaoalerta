import { useEffect, useRef, useState } from 'react'
import { useAcessibilidade } from '../lib/acessibilidade'

/** Leitura em voz alta com a síntese de fala do próprio aparelho (funciona sem internet quando a voz pt-BR está instalada). */
export function vozDisponivel() {
  return typeof window !== 'undefined' && 'speechSynthesis' in window && 'SpeechSynthesisUtterance' in window
}

function escolherVoz(): SpeechSynthesisVoice | undefined {
  const vozes = window.speechSynthesis.getVoices()
  return vozes.find((v) => v.lang === 'pt-BR' && v.localService) ?? vozes.find((v) => v.lang === 'pt-BR') ?? vozes.find((v) => v.lang.startsWith('pt'))
}

type Estado = 'parado' | 'falando' | 'pausado'

export function LeitorVoz({ texto, rotulo = 'Ouvir', compacto = false }: { texto: string; rotulo?: string; compacto?: boolean }) {
  const { prefs, alterar } = useAcessibilidade()
  const [estado, setEstado] = useState<Estado>('parado')
  const [semVozPt, setSemVozPt] = useState(false)
  const atual = useRef<SpeechSynthesisUtterance | null>(null)

  useEffect(() => () => { if (vozDisponivel() && atual.current) window.speechSynthesis.cancel() }, [])

  if (!vozDisponivel()) return <p className="text-[13px] text-tinta-suave">Leitura por voz indisponível neste navegador.</p>

  const falar = () => {
    const s = window.speechSynthesis
    s.cancel()
    const u = new SpeechSynthesisUtterance(texto)
    u.lang = 'pt-BR'
    u.rate = prefs.velocidadeVoz
    const voz = escolherVoz()
    if (voz) u.voice = voz
    setSemVozPt(!voz)
    u.onend = () => setEstado('parado')
    u.onerror = () => setEstado('parado')
    atual.current = u
    s.speak(u)
    setEstado('falando')
  }
  const pausar = () => { window.speechSynthesis.pause(); setEstado('pausado') }
  const continuar = () => { window.speechSynthesis.resume(); setEstado('falando') }
  const parar = () => { window.speechSynthesis.cancel(); setEstado('parado') }

  const botao = 'min-h-11 rounded-controle border border-linha bg-branco px-3 py-2 text-[14px] font-semibold text-petroleo hover:bg-areia'
  return (
    <div className={`flex flex-wrap items-center gap-2 ${compacto ? '' : 'mt-3'}`} role="group" aria-label={`Leitura por voz: ${rotulo}`}>
      {estado === 'parado' && <button type="button" className={botao} onClick={falar}>🔊 {rotulo}</button>}
      {estado === 'falando' && <button type="button" className={botao} onClick={pausar}>Pausar</button>}
      {estado === 'pausado' && <button type="button" className={botao} onClick={continuar}>Continuar</button>}
      {estado !== 'parado' && <button type="button" className={botao} onClick={parar}>Parar</button>}
      {estado !== 'parado' && <button type="button" className={botao} onClick={falar}>Repetir</button>}
      {!compacto && (
        <label className="flex items-center gap-1.5 text-[13px] text-tinta-suave">
          Velocidade
          <select className="min-h-11 rounded-controle border border-linha bg-branco px-2 text-[14px]" value={prefs.velocidadeVoz}
            onChange={(e) => alterar({ velocidadeVoz: Number(e.target.value) })}>
            <option value={0.75}>Lenta</option><option value={1}>Normal</option><option value={1.25}>Rápida</option>
          </select>
        </label>
      )}
      {semVozPt && estado !== 'parado' && <span className="text-[12.5px] text-atencao">Voz em português não instalada neste aparelho; usando a voz padrão.</span>}
    </div>
  )
}
