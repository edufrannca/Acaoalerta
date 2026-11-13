import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { Botao, Cartao, Fonte, Selo, Vazio, classeEntrada } from '../componentes/Base'
import { ContatosEmergencia } from '../componentes/Contatos'
import { api, formatarHora, formatarRelativo } from '../lib/api'
import { useDados, useLocalizacao, type Posicao } from '../lib/hooks'
import { MUNICIPIOS, nomeMunicipio } from '../lib/municipios'
import { useSessao } from '../lib/sessao'
import { iniciarSocorro } from '../lib/socorro'
import { useEvento } from '../lib/tempoReal'

interface Estado { tipo: string; titulo: string; detalhe: string; fonte: { nome: string; atualizadoEm: string | null; oficial: boolean; demonstrativa: boolean } | null }
interface Contexto {
  municipio: { codigo: string; nome: string } | null
  foraDaRmr: boolean
  estados: Estado[]
  orientacoes: string[]
  limitacoes: string[]
  estacaoMaisProxima: { nome: string; ultimaLeituraEm: string | null; mm24h: number | null; mm1h: number | null; distanciaKm: number; estado: string; fonte: string } | null
  avaliadoEm: string
  aviso: string
}
interface Aviso { id: string; titulo: string; oficial: boolean; severidade: string; inicioEm: string; fimEm: string; municipios: string[]; instrucoes: string[]; fonteNome: string; rotulo: string; cor: string | null }
interface Notificacao { id: string; titulo: string; mensagem: string; pergunta: string; avisoOficial: boolean; criadaEm: string; resposta: string | null; aviso: { id: string; instrucoes: string[] } | null }

const tomEstado: Record<string, 'erro' | 'atencao' | 'espera' | 'neutro' | 'demo'> = {
  SuscetibilidadeConhecida: 'atencao', AvisoOficialVigente: 'erro', AvisoDemonstrativoVigente: 'demo',
  OcorrenciaRelatada: 'espera', OcorrenciaVerificada: 'erro', InformacoesDesatualizadas: 'neutro', CondicaoDesconhecida: 'neutro',
}

export function Inicio() {
  const navegar = useNavigate()
  const { usuario } = useSessao()
  const { posicao, estado: gps, iniciar } = useLocalizacao()
  const [municipioManual, setMunicipioManual] = useState<string>(() => localStorage.getItem('alerta.municipio') ?? '')
  const [abrindo, setAbrindo] = useState(false)

  const ctx = useDados<Contexto>(posicao ? `/api/territorio/contexto?lat=${posicao.latitude.toFixed(5)}&lon=${posicao.longitude.toFixed(5)}` : null, { cache: true })
  const municipio = ctx.dados?.municipio?.codigo ?? (municipioManual || usuario?.municipioCodigo || null)
  const avisos = useDados<Aviso[]>('/api/alertas/vigentes', { cache: true, intervaloMs: 60000 })
  const notifs = useDados<Notificacao[]>(usuario ? '/api/eu/notificacoes' : null, { intervaloMs: 30000 })

  useEvento('avisoPublicado', () => { void avisos.recarregar(); void notifs.recarregar() })
  useEvento('notificacao', () => { void notifs.recarregar() })

  const pedirSocorro = useCallback(async (avisoId?: string) => {
    setAbrindo(true)
    const id = await iniciarSocorro(posicao, avisoId)
    navegar(`/socorro/${id}`)
  }, [posicao, navegar])

  const pendente = notifs.dados?.find((n) => !n.resposta && Date.now() - new Date(n.criadaEm).getTime() < 24 * 3600 * 1000)
  const avisosAqui = (avisos.dados ?? []).filter((a) => !municipio || a.municipios.includes(municipio) || a.municipios.length === 0)

  return (
    <div className="grid gap-4 md:grid-cols-[1.15fr_1fr]">
      <div className="grid content-start gap-4">
        {pendente && <PerguntaProativa n={pendente} aoSocorro={() => pedirSocorro(pendente.aviso?.id)} aoResponder={() => notifs.recarregar()} />}

        <section aria-labelledby="titulo-acoes" className="grid gap-3">
          <h1 id="titulo-acoes" className="sr-only">Ações rápidas</h1>
          <button onClick={() => pedirSocorro()} disabled={abrindo}
            className="botao-socorro flex min-h-32 w-full flex-col items-start justify-center rounded-cartao bg-emergencia px-6 py-5 text-left text-branco hover:bg-emergencia-escura disabled:opacity-80">
            <span className="text-[30px] font-bold leading-none tracking-tight">Preciso de socorro</span>
            <span className="mt-2 text-[15px] text-branco/90">Um toque registra o pedido com sua localização. Os detalhes vêm depois.</span>
          </button>
          <div className="grid grid-cols-2 gap-3">
            <Link to="/seguro" className="flex min-h-24 flex-col justify-center rounded-cartao bg-seguro px-4 py-3 text-branco hover:brightness-95">
              <span className="text-[20px] font-bold leading-tight">Estou seguro</span>
              <span className="text-[13px] text-branco/85">Avise que está bem</span>
            </Link>
            <Link to="/ajudar" className="flex min-h-24 flex-col justify-center rounded-cartao bg-oceano px-4 py-3 text-branco hover:brightness-95">
              <span className="text-[20px] font-bold leading-tight">Quero ajudar</span>
              <span className="text-[13px] text-branco/85">Doações e voluntariado</span>
            </Link>
          </div>
        </section>

        <Cartao titulo="Sua localização">
          <Localizacao posicao={posicao} gps={gps} iniciar={iniciar} municipioIdentificado={ctx.dados?.municipio?.nome ?? null} foraDaRmr={!!ctx.dados?.foraDaRmr} />
          {!posicao && (
            <div className="mt-3">
              <label className="block text-[14px] font-medium" htmlFor="mun-manual">Ou escolha seu município para ver avisos</label>
              <select id="mun-manual" className={`${classeEntrada} mt-1`} value={municipioManual}
                onChange={(e) => { setMunicipioManual(e.target.value); localStorage.setItem('alerta.municipio', e.target.value) }}>
                <option value="">Selecione</option>
                {Object.entries(MUNICIPIOS).map(([c, n]) => <option key={c} value={c}>{n}</option>)}
              </select>
            </div>
          )}
        </Cartao>

        <Cartao titulo="Avisos para a sua região" acao={avisos.doCache ? <Selo tom="atencao">Cópia salva {formatarRelativo(avisos.doCache)}</Selo> : undefined}>
          {avisosAqui.length === 0 ? (
            <Vazio>Nenhum aviso vigente {municipio ? `para ${nomeMunicipio(municipio)}` : 'na RMR'} nas fontes consultadas. Isso não significa ausência de risco.</Vazio>
          ) : (
            <ul className="grid gap-2">
              {avisosAqui.map((a) => (
                <li key={a.id} className={`rounded-controle border-l-4 p-3 ${a.oficial ? 'border-emergencia bg-emergencia-claro/60' : 'border-[#6E5A95] bg-[#F3F0F8]'}`}>
                  <div className="flex flex-wrap items-center gap-2">
                    <Selo tom={a.oficial ? 'erro' : 'demo'}>{a.oficial ? 'Aviso oficial' : 'Demonstrativo, não oficial'}</Selo>
                    <span className="text-[13px] text-tinta-suave">{a.severidade} até {formatarHora(a.fimEm)}</span>
                  </div>
                  <p className="mt-1 font-semibold text-petroleo">{a.titulo}</p>
                  {a.instrucoes[0] && <p className="mt-1 text-[14px]">{a.instrucoes[0]}</p>}
                  <Fonte nome={a.fonteNome} oficial={a.oficial} demonstrativa={!a.oficial} />
                </li>
              ))}
            </ul>
          )}
        </Cartao>
      </div>

      <div className="grid content-start gap-4">
        <RegiaoAtual ctx={ctx.dados} doCache={ctx.doCache} temPosicao={!!posicao} />
        <AbrigosProximos posicao={posicao} municipio={municipio} />
        <Cartao titulo="Canais de emergência"><ContatosEmergencia compacto /></Cartao>
        <Cartao titulo="Prepare-se">
          <ul className="grid list-disc gap-1.5 pl-5 text-[14.5px]">
            {(ctx.dados?.orientacoes ?? [
              'Tenha à mão documentos, remédios de uso contínuo, lanterna e carregador em saco plástico.',
              'Combine com a família um ponto de encontro seguro e um contato de referência.',
              'Não atravesse ruas alagadas, a pé ou de carro.',
            ]).slice(0, 6).map((o) => <li key={o}>{o}</li>)}
          </ul>
          <Fonte nome="Orientações preventivas aprovadas, adaptadas de orientações públicas de Defesa Civil" />
        </Cartao>
      </div>
    </div>
  )
}

function Localizacao({ posicao, gps, iniciar, municipioIdentificado, foraDaRmr }: { posicao: Posicao | null; gps: string; iniciar: () => void; municipioIdentificado: string | null; foraDaRmr: boolean }) {
  if (posicao) {
    return (
      <div>
        <p className="text-[20px] font-semibold text-petroleo">{foraDaRmr ? 'Fora da RMR' : municipioIdentificado ?? 'Identificando município…'}</p>
        <p className="numero-tabular text-[13px] text-tinta-suave">
          {posicao.latitude.toFixed(5)}, {posicao.longitude.toFixed(5)} · precisão ±{posicao.precisao ?? '?'} m · leitura {formatarRelativo(posicao.lidaEm)}
        </p>
        <p className="mt-1 text-[12.5px] text-tinta-suave">Atualizada enquanto o app está aberto. Não acompanhamos sua posição em segundo plano.</p>
      </div>
    )
  }
  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <p className="text-[14.5px]">
        {gps === 'negado' ? 'Permissão de localização negada. Você ainda pode pedir socorro e informar uma referência.'
          : gps === 'indisponivel' ? 'Localização indisponível neste aparelho agora.'
          : gps === 'solicitando' ? 'Aguardando o GPS…' : 'Use sua localização para ver riscos conhecidos e o município.'}
      </p>
      {gps !== 'solicitando' && <Botao variante="secundario" onClick={iniciar}>Usar minha localização</Botao>}
    </div>
  )
}

function RegiaoAtual({ ctx, doCache, temPosicao }: { ctx: Contexto | null; doCache: string | null; temPosicao: boolean }) {
  return (
    <Cartao titulo="Riscos conhecidos neste ponto" acao={doCache ? <Selo tom="atencao">Cópia salva</Selo> : undefined}>
      {!temPosicao ? <Vazio>Ative a localização para cruzarmos seu ponto com áreas de risco, avisos e chuva.</Vazio>
        : !ctx ? <p className="text-[14px] text-tinta-suave">Consultando fontes…</p> : (
          <>
            <ul className="grid gap-2.5">
              {ctx.estados.map((e, i) => (
                <li key={i}>
                  <Selo tom={tomEstado[e.tipo] ?? 'neutro'}>{e.titulo}</Selo>
                  <p className="mt-1 text-[14.5px]">{e.detalhe}</p>
                  {e.fonte && <Fonte nome={e.fonte.nome} em={e.fonte.atualizadoEm} oficial={e.fonte.oficial} demonstrativa={e.fonte.demonstrativa} />}
                </li>
              ))}
            </ul>
            {ctx.estacaoMaisProxima && (
              <div className="mt-3 rounded-controle bg-areia p-3 text-[14px]">
                <p><span className="font-semibold">Chuva na estação mais próxima</span> ({ctx.estacaoMaisProxima.nome}, {ctx.estacaoMaisProxima.distanciaKm} km):{' '}
                  {ctx.estacaoMaisProxima.mm24h == null ? 'sem leitura' : <span className="numero-tabular">{ctx.estacaoMaisProxima.mm24h} mm em 24 h</span>}</p>
                <Fonte nome={ctx.estacaoMaisProxima.fonte} em={ctx.estacaoMaisProxima.ultimaLeituraEm} />
                {ctx.estacaoMaisProxima.estado !== 'Atual' && <p className="mt-1 font-medium text-atencao">Leitura antiga: não é informação em tempo real.</p>}
              </div>
            )}
            <p className="mt-3 text-[13px] font-medium text-petroleo">{ctx.aviso}</p>
            {ctx.limitacoes.map((l) => <p key={l} className="mt-1 text-[12.5px] text-tinta-suave">{l}</p>)}
          </>
        )}
    </Cartao>
  )
}

interface Abrigo { id: string; nome: string; endereco: string | null; latitude: number; longitude: number; disponibilidade: string; ficticio: boolean; fonte: string; municipioCodigo: string; acessibilidade: string }

function AbrigosProximos({ posicao, municipio }: { posicao: Posicao | null; municipio: string | null }) {
  const { dados, doCache } = useDados<{ abrigos: Abrigo[]; observacao: string }>('/api/abrigos', { cache: true })
  const dist = (a: Abrigo) => posicao ? Math.hypot((a.latitude - posicao.latitude) * 111, (a.longitude - posicao.longitude) * 110) : 0
  const lista = (dados?.abrigos ?? []).filter((a) => posicao || !municipio || a.municipioCodigo === municipio).sort((a, b) => dist(a) - dist(b)).slice(0, 3)
  return (
    <Cartao titulo="Abrigos e pontos de apoio" acao={<Link to="/mapa" className="text-[14px] font-medium text-oceano underline-offset-2 hover:underline">Ver no mapa</Link>}>
      {lista.length === 0 ? <Vazio>Sem abrigos na base consultada para esta área.</Vazio> : (
        <ul className="grid gap-2">
          {lista.map((a) => (
            <li key={a.id} className="rounded-controle bg-areia p-3">
              <p className="font-semibold text-petroleo">{a.nome}</p>
              <p className="text-[13.5px]">{a.endereco ?? 'Endereço não informado'} · {nomeMunicipio(a.municipioCodigo)}{posicao ? ` · ${dist(a).toFixed(1)} km` : ''}</p>
              <p className="mt-1 text-[13px] font-medium text-atencao">{a.disponibilidade}</p>
              <Fonte nome={a.fonte} demonstrativa={a.ficticio} />
            </li>
          ))}
        </ul>
      )}
      <p className="mt-2 text-[12.5px] text-tinta-suave">{dados?.observacao}{doCache ? ' Lista salva no aparelho.' : ''}</p>
    </Cartao>
  )
}

function PerguntaProativa({ n, aoSocorro, aoResponder }: { n: Notificacao; aoSocorro: () => void; aoResponder: () => void }) {
  const navegar = useNavigate()
  const [orientacoes, setOrientacoes] = useState(false)
  const responder = async (resposta: string) => { try { await api(`/api/eu/notificacoes/${n.id}/resposta`, { method: 'POST', json: { resposta } }) } catch { /* segue mesmo sem rede */ } }
  useEffect(() => { if ('vibrate' in navigator) navigator.vibrate?.(200) }, [n.id])
  return (
    <section role="alertdialog" aria-labelledby="pergunta-titulo" className={`rounded-cartao p-5 text-branco ${n.avisoOficial ? 'bg-petroleo' : 'bg-[#3E2F5E]'}`}>
      <h2 id="pergunta-titulo" className="text-[22px] font-bold">{n.titulo}</h2>
      <p className="mt-1 text-[15px] text-areia/90">{n.mensagem}</p>
      <p className="mt-3 text-[19px] font-semibold">{n.pergunta}</p>
      <div className="mt-3 grid gap-2 sm:grid-cols-3">
        <Botao variante="seguro" onClick={async () => { await responder('EstouSeguro'); aoResponder(); navegar(`/seguro?aviso=${n.aviso?.id ?? ''}`) }}>Estou seguro</Botao>
        <Botao variante="perigo" onClick={async () => { await responder('PrecisoDeSocorro'); aoSocorro() }}>Preciso de socorro</Botao>
        <Botao variante="secundario" onClick={async () => { setOrientacoes(true); await responder('VerOrientacoes') }}>Ver orientações</Botao>
      </div>
      {orientacoes && n.aviso && (
        <ul className="mt-3 list-disc pl-5 text-[14.5px]">{n.aviso.instrucoes.map((i) => <li key={i}>{i}</li>)}</ul>
      )}
    </section>
  )
}
