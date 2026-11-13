import { useEffect, useRef, useState } from 'react'
import { formatarHora } from '../lib/api'
import { useAcessibilidade } from '../lib/acessibilidade'
import { useDados } from '../lib/hooks'
import { useEvento } from '../lib/tempoReal'
import { IcAlerta, IcChuva } from './Icones'
import { LeitorVoz } from './LeitorVoz'

export type NivelAlagamento = 'SemEvidenciaSuficiente' | 'Atencao' | 'Elevado' | 'Critico'
export interface IndicadorAlagamento {
  regiao: string; nivel: NivelAlagamento; rotulo: string
  explicacao: string; explicacaoOrigem: 'ia_validada' | 'regras'; explicacaoObservacao: string | null
  justificativa: string[]
  estacao: { nome: string; fonte: string; distanciaMetros: number; leituraEm: string | null; atual: boolean; mm1h: number | null; mm3h: number | null; mm6h: number | null; mm12h: number | null; mm24h: number | null; mm48h: number | null; mm72h: number | null } | null
  intensidade: string; tendencia: string
  avisos: { titulo: string; severidade: string; oficial: boolean; fonte: string; fimEm: string }[]
  ocorrenciasVerificadas: number; relatosNaoVerificados: number; suscetibilidadeDocumentada: string | null; estacoesDivergentes: boolean
  estimativaTemporal: { disponivel: boolean; mensagem: string; janela?: string | null; incerteza?: string | null }
  separacao: { chuvaObservada: string; previsaoOficial: string; estimativaExperimental: string; alagamentoConfirmado: string }
  confiabilidade: { nivel: string; motivos: string[] }
  limitacoes: string[]; orientacoes: string[]
  avaliadoEm: string; validoAte: string; criterios: string; fontes: string[]
}

const ORDEM: Record<NivelAlagamento, number> = { SemEvidenciaSuficiente: 0, Atencao: 1, Elevado: 2, Critico: 3 }
// Cor + ícone + texto + número de barras + estilo de borda: o nível nunca depende só da cor (daltonismo, alto contraste).
const ESTILO: Record<NivelAlagamento, { caixa: string; barra: string; borda: string }> = {
  SemEvidenciaSuficiente: { caixa: 'bg-fundo text-tinta', barra: 'bg-tinta-suave', borda: 'border-l-4 border-dotted border-tinta-suave' },
  Atencao: { caixa: 'bg-atencao-claro text-atencao', barra: 'bg-atencao', borda: 'border-l-4 border-dashed border-atencao' },
  Elevado: { caixa: 'bg-emergencia-claro text-emergencia-escura', barra: 'bg-emergencia-escura', borda: 'border-l-[6px] border-solid border-emergencia-escura' },
  Critico: { caixa: 'bg-emergencia-escura text-branco', barra: 'bg-branco', borda: 'border-l-[8px] border-double border-emergencia-escura' },
}

const mm = (v: number | null | undefined) => v == null ? 'sem leitura' : `${String(v).replace('.', ',')} mm`
const km = (m: number) => m < 1000 ? `${m} m` : `${(m / 1000).toFixed(1).replace('.', ',')} km`

function sinal(prefs: { som: boolean; vibrar: boolean }) {
  if (prefs.vibrar) try { navigator.vibrate?.([200, 100, 200]) } catch { /* sem suporte */ }
  if (prefs.som) try {
    const C = window.AudioContext ?? (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext
    const c = new C(); const o = c.createOscillator(); const g = c.createGain()
    o.frequency.value = 880; g.gain.value = 0.15; o.connect(g); g.connect(c.destination); o.start(); o.stop(c.currentTime + 0.25)
  } catch { /* áudio bloqueado */ }
}

export function textoFala(i: IndicadorAlagamento) {
  const e = i.estacao
  return [
    `Condições de chuva e atenção a alagamentos em ${i.regiao}. Nível: ${i.rotulo}.`,
    i.explicacao,
    e ? `Estação ${e.nome}, a ${km(e.distanciaMetros)}: ${mm(e.mm1h)} na última hora, ${mm(e.mm24h)} em 24 horas. ${i.intensidade}. ${i.tendencia}.${e.atual ? '' : ' Leitura antiga.'}` : 'Sem estação de chuva com leitura utilizável por perto.',
    i.estimativaTemporal.mensagem,
    ...i.orientacoes.slice(0, 3),
  ].join(' ')
}

/** Seção "Condições de chuva e atenção a alagamentos" — indicador determinístico; IA só explica. */
export function CondicoesAlagamento({ latitude, longitude, regiao, titulo = 'Condições de chuva e atenção a alagamentos', compacto = false }: {
  latitude: number | null; longitude: number | null; regiao?: string | null; titulo?: string; compacto?: boolean
}) {
  const { prefs } = useAcessibilidade()
  const url = latitude != null && longitude != null
    ? `/api/territorio/alagamento?lat=${latitude.toFixed(5)}&lon=${longitude.toFixed(5)}${regiao ? `&regiao=${encodeURIComponent(regiao)}` : ''}` : null
  const { dados: i, doCache, erro, recarregar } = useDados<IndicadorAlagamento>(url, { cache: true, intervaloMs: 5 * 60_000 })
  useEvento('condicoesAtualizadas', () => { void recarregar() })
  useEvento('notificacao', (d) => { if ((d as { tipo?: string })?.tipo === 'preventivo') void recarregar() })

  // Anúncio para leitor de tela e sinal só quando o nível MUDA (sem repetir a cada atualização).
  const anterior = useRef<NivelAlagamento | null>(null)
  const [anuncio, setAnuncio] = useState('')
  useEffect(() => {
    if (!i) return
    const ant = anterior.current
    if (ant && ant !== i.nivel) {
      setAnuncio(`Nível de atenção a alagamentos mudou para ${i.rotulo} em ${i.regiao}.`)
      if (ORDEM[i.nivel] > ORDEM[ant] && ORDEM[i.nivel] >= 2) sinal(prefs)
    }
    anterior.current = i.nivel
  }, [i?.nivel]) // eslint-disable-line react-hooks/exhaustive-deps

  const idT = `titulo-alag-${compacto ? 'c' : 'n'}`
  return (
    <section className="rounded-cartao bg-branco p-4 ring-1 ring-linha sm:p-5" aria-labelledby={idT}>
      <div className="flex items-start gap-3">
        <span className="grid size-12 shrink-0 place-items-center rounded-controle bg-oceano-suave text-oceano" aria-hidden="true"><IcChuva tamanho={28} /></span>
        <div className="min-w-0 flex-1">
          <h2 id={idT} className="text-[19px] font-bold text-petroleo">{titulo}</h2>
          <p className="text-[13.5px] text-tinta-suave">Chuva medida pela APAC e avisos do INMET.</p>
        </div>
      </div>
      <p className="sr-only" role="status" aria-live={i?.nivel === 'Critico' ? 'assertive' : 'polite'}>{anuncio}</p>

      {!url ? <p className="mt-3 text-[15px]">Ative a localização ou escolha um local monitorado para ver as condições.</p>
        : !i ? <p className="mt-3 text-[15px] text-tinta-suave">{erro ? 'Não foi possível consultar agora. Tentaremos de novo em instantes.' : 'Consultando fontes…'}</p> : (
          <div className="mt-3 grid gap-3">
            <div className={`rounded-controle p-3 ${ESTILO[i.nivel].caixa} ${ESTILO[i.nivel].borda}`} data-nivel={i.nivel}>
              <div className="flex items-center gap-2">
                {i.nivel !== 'SemEvidenciaSuficiente' && <IcAlerta tamanho={24} />}
                <p className="text-[18px] font-bold">
                  <span className="sr-only">Nível de atenção a alagamentos: </span>{i.rotulo}
                  <span className="ml-1 text-[14px] font-semibold">({ORDEM[i.nivel]} de 3)</span>
                </p>
                <span className="ml-auto flex gap-1" aria-hidden="true">
                  {[1, 2, 3].map((n) => <span key={n} className={`h-4 w-2 rounded-sm ${n <= ORDEM[i.nivel] ? ESTILO[i.nivel].barra : 'bg-linha'}`} />)}
                </span>
              </div>
              <p className="mt-1 text-[15px]">{i.regiao}</p>
            </div>

            {(!compacto || i.nivel !== 'SemEvidenciaSuficiente') && <p className="text-[16px] leading-relaxed">{i.explicacao}</p>}
            {doCache && <p className="text-[13.5px] font-semibold text-atencao">Cópia salva no aparelho: pode estar desatualizada.</p>}

            <dl className="grid gap-2 text-[15px] sm:grid-cols-2">
              <div><dt className="font-semibold">Chuva observada</dt>
                <dd>{i.estacao ? <>{mm(i.estacao.mm1h)} em 1 h · {mm(i.estacao.mm3h)} em 3 h · {mm(i.estacao.mm24h)} em 24 h{!compacto && <> · {mm(i.estacao.mm72h)} em 72 h</>}</> : 'Sem leitura utilizável'}</dd></div>
              <div><dt className="font-semibold">Estação e distância</dt>
                <dd>{i.estacao ? `${i.estacao.nome}, a ${km(i.estacao.distanciaMetros)}${i.estacao.leituraEm ? ` · leitura ${formatarHora(i.estacao.leituraEm)}` : ''}` : '—'}
                  {i.estacao && !i.estacao.atual && <strong className="block text-atencao">Leitura antiga: não é tempo real.</strong>}</dd></div>
              {(!compacto || i.nivel !== 'SemEvidenciaSuficiente') && <div><dt className="font-semibold">Intensidade e tendência</dt><dd>{i.intensidade}. {i.tendencia}.</dd></div>}
              {(!compacto || i.avisos.length > 0) && <div><dt className="font-semibold">Aviso oficial</dt><dd>{i.separacao.previsaoOficial}</dd></div>}
              {(!compacto || i.ocorrenciasVerificadas > 0) && <div><dt className="font-semibold">Alagamento confirmado</dt><dd>{i.separacao.alagamentoConfirmado}</dd></div>}
              {i.nivel !== 'SemEvidenciaSuficiente' && <div><dt className="font-semibold">Quando pode alagar</dt><dd>{i.estimativaTemporal.mensagem}</dd></div>}
            </dl>

            {!compacto && (
              <details className="rounded-controle bg-fundo p-3 text-[14.5px]">
                <summary className="min-h-11 cursor-pointer content-center font-semibold">Por que este nível, confiança e limitações</summary>
                <ul className="mt-2 grid list-disc gap-1 pl-5">{i.justificativa.map((j) => <li key={j}>{j}</li>)}</ul>
                {i.suscetibilidadeDocumentada && <p className="mt-2">{i.suscetibilidadeDocumentada}</p>}
                <p className="mt-2"><strong>Confiança: {i.confiabilidade.nivel}.</strong> {i.confiabilidade.motivos.join(' ')}</p>
                <ul className="mt-2 grid list-disc gap-1 pl-5 text-tinta-suave">{i.limitacoes.map((l) => <li key={l}>{l}</li>)}</ul>
                <p className="mt-2 text-tinta-suave">Explicação {i.explicacaoOrigem === 'ia_validada' ? 'redigida por IA e conferida com os dados' : 'gerada pelas regras do sistema'}{i.explicacaoObservacao ? ` (${i.explicacaoObservacao})` : ''}. Critérios {i.criterios}.</p>
              </details>
            )}

            <div>
              <p className="text-[15px] font-semibold">O que fazer</p>
              <ul className="mt-1 grid list-disc gap-1 pl-5 text-[15px]">{i.orientacoes.slice(0, compacto ? 3 : 5).map((o) => <li key={o}>{o}</li>)}</ul>
            </div>
            <p className="text-[13px] text-tinta-suave">Fontes: APAC e INMET · atualizado às {formatarHora(i.avaliadoEm)}</p>
            <LeitorVoz rotulo="Ouvir informações" texto={textoFala(i)} destaque />
          </div>
        )}
    </section>
  )
}
