import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { Botao, Cartao, Fonte, Selo, classeEntrada } from '../componentes/Base'
import { LeitorVoz } from '../componentes/LeitorVoz'
import { ContatosEmergencia } from '../componentes/Contatos'
import { CartaoPedido, FormOferta } from '../componentes/PedidosComunidade'
import { Mapa } from '../componentes/Mapa'
import { IcAbrigo, IcAjudar, IcAlerta, IcChuva, IcLocal, IcSeguro, IcSeta, IcTelefone } from '../componentes/Icones'
import { api, formatarHora, formatarRelativo } from '../lib/api'
import { useDados, type Posicao } from '../lib/hooks'
import { usePedidosComunitarios } from '../lib/comunidade'
import { MUNICIPIOS, nomeMunicipio } from '../lib/municipios'
import { abrigosProximos, descreverEstacao, formatarKm, rotuloLocal, useRegiao, type Contexto, type RespostaAbrigos } from '../lib/regiao'
import { useSessao } from '../lib/sessao'
import { iniciarSocorro } from '../lib/socorro'
import { useEvento } from '../lib/tempoReal'

interface Aviso { id: string; titulo: string; oficial: boolean; severidade: string; inicioEm: string; fimEm: string; municipios: string[]; instrucoes: string[]; riscos?: string[]; fonteNome: string; rotulo: string; cor: string | null; poligono: unknown }
interface Notificacao { id: string; titulo: string; mensagem: string; pergunta: string; avisoOficial: boolean; criadaEm: string; resposta: string | null; aviso: { id: string; instrucoes: string[] } | null }

const tomEstado: Record<string, 'erro' | 'atencao' | 'espera' | 'neutro' | 'demo'> = {
  SuscetibilidadeConhecida: 'atencao', AvisoOficialVigente: 'erro', AvisoDemonstrativoVigente: 'demo',
  OcorrenciaRelatada: 'espera', OcorrenciaVerificada: 'erro', InformacoesDesatualizadas: 'neutro', CondicaoDesconhecida: 'neutro',
}

export function Inicio() {
  const navegar = useNavigate()
  const { usuario } = useSessao()
  const { posicao, gps, iniciar, ctx, local } = useRegiao()
  const [municipioManual, setMunicipioManual] = useState<string>(() => { try { return localStorage.getItem('alerta.municipio') ?? '' } catch { return '' } })
  const [abrindo, setAbrindo] = useState(false)

  const municipio = ctx.dados?.municipio?.codigo ?? (municipioManual || usuario?.municipioCodigo || null)
  const avisos = useDados<Aviso[]>('/api/alertas/vigentes', { cache: true, intervaloMs: 60000 })
  const notifs = useDados<Notificacao[]>(usuario ? '/api/eu/notificacoes' : null, { intervaloMs: 30000 })
  const abrigos = useDados<RespostaAbrigos>('/api/abrigos', { cache: true, intervaloMs: 10 * 60 * 1000 })

  useEvento('avisoPublicado', () => { void avisos.recarregar(); void notifs.recarregar() })
  useEvento('notificacao', () => { void notifs.recarregar() })

  const pedirSocorro = useCallback(async (avisoId?: string) => {
    setAbrindo(true)
    const id = await iniciarSocorro(posicao, avisoId)
    navegar(`/socorro/${id}`)
  }, [posicao, navegar])

  const pendente = notifs.dados?.find((n) => !n.resposta && Date.now() - new Date(n.criadaEm).getTime() < 24 * 3600 * 1000)
  const avisosAqui = (avisos.dados ?? []).filter((a) => !municipio || a.municipios.includes(municipio) || a.municipios.length === 0)
  const local1 = rotuloLocal(local.dados, ctx.dados)

  return (
    <div className="grid gap-4 lg:grid-cols-[1.1fr_1fr] lg:gap-6">
      <div className="grid content-start gap-4">
        {pendente && <PerguntaProativa n={pendente} aoSocorro={() => pedirSocorro(pendente.aviso?.id)} aoResponder={() => notifs.recarregar()} />}

        <section aria-labelledby="titulo-acoes">
          <h1 id="titulo-acoes" className="sr-only">Ações rápidas</h1>
          <div className="grid grid-cols-[1.3fr_1fr_1fr] gap-2.5 sm:gap-3">
            <button onClick={() => pedirSocorro()} disabled={abrindo} aria-describedby="dica-socorro"
              className="flex min-h-36 flex-col items-center justify-center gap-2 rounded-cartao bg-emergencia px-2 py-4 text-center text-branco shadow-[0_6px_18px_rgba(168,50,50,0.35)] ring-4 ring-emergencia-claro hover:bg-emergencia-escura disabled:opacity-80 sm:min-h-40">
              <IcTelefone tamanho={38} />
              <span className="text-[21px] font-extrabold leading-[1.1] sm:text-[24px]">Preciso de socorro</span>
            </button>
            <Link to="/seguro" className="flex min-h-36 flex-col items-center justify-center gap-2 rounded-cartao bg-seguro-vivo px-2 py-4 text-center text-branco hover:brightness-95 sm:min-h-40">
              <IcSeguro tamanho={34} />
              <span className="text-[18px] font-bold leading-[1.1] sm:text-[20px]">Estou seguro</span>
            </Link>
            <Link to="/ajudar" className="flex min-h-36 flex-col items-center justify-center gap-2 rounded-cartao bg-oceano px-2 py-4 text-center text-branco hover:brightness-95 sm:min-h-40">
              <IcAjudar tamanho={34} />
              <span className="text-[18px] font-bold leading-[1.1] sm:text-[20px]">Quero ajudar</span>
            </Link>
          </div>
          <p id="dica-socorro" className="mt-2 text-[13px] text-tinta-suave">Um toque em “Preciso de socorro” registra o pedido com sua localização; os detalhes vêm depois.</p>
        </section>

        {/* Localização */}
        <section className="rounded-cartao bg-branco ring-1 ring-linha" aria-labelledby="titulo-local">
          <Link to="/mapa" className="flex items-center gap-3 p-4">
            <span className="text-oceano"><IcLocal tamanho={30} /></span>
            <span className="min-w-0 flex-1">
              <span id="titulo-local" className="block text-[14px] text-tinta-suave">Sua localização</span>
              <span className="block truncate text-[21px] font-bold text-petroleo">
                {posicao ? (local1 ?? 'Identificando município…') : municipioManual ? `${nomeMunicipio(municipioManual)} - PE (escolhido)` : 'Localização desativada'}
              </span>
            </span>
            <span className="text-tinta-suave"><IcSeta /></span>
          </Link>
          <div className="border-t border-linha px-4 py-3">
            {posicao ? (
              <>
                <p className="flex items-center gap-2 text-[15px] font-medium"><span className="size-2.5 rounded-full bg-seguro-vivo" aria-hidden="true" />Localização ativa (atualiza com o app aberto)</p>
                <p className="numero-tabular mt-0.5 text-[13px] text-tinta-suave">Precisão ±{posicao.precisao ?? '?'} m · última leitura {formatarRelativo(posicao.lidaEm)}{local.dados?.precisao === 'municipio' ? ' · bairro não confirmado' : ''}</p>
              </>
            ) : (
              <div className="grid gap-2">
                <p className="text-[14.5px]">{gps === 'negado' ? 'Permissão de localização negada. Você ainda pode pedir socorro e informar uma referência.' : gps === 'indisponivel' ? 'GPS indisponível agora.' : gps === 'solicitando' ? 'Aguardando o GPS…' : 'Ative para ver riscos conhecidos, chuva e abrigos perto de você.'}</p>
                <div className="flex flex-wrap items-center gap-2">
                  {gps !== 'solicitando' && <Botao variante="primario" onClick={iniciar}>Usar minha localização</Botao>}
                  <label className="sr-only" htmlFor="mun-manual">Ou escolha seu município</label>
                  <select id="mun-manual" className={`${classeEntrada} w-auto min-w-48`} value={municipioManual}
                    onChange={(e) => { setMunicipioManual(e.target.value); try { localStorage.setItem('alerta.municipio', e.target.value) } catch { /* privado */ } }}>
                    <option value="">Ou escolha o município</option>
                    {Object.entries(MUNICIPIOS).map(([c, n]) => <option key={c} value={c}>{n}</option>)}
                  </select>
                </div>
              </div>
            )}
          </div>
        </section>

        <ClimaRisco ctx={ctx.dados} doCache={ctx.doCache} temPosicao={!!posicao} />
        <PedidosProximosInicio posicao={posicao} municipio={municipio} />
        <div className="hidden lg:block">
          <Mapa camadas={['limites', 'avisos', 'chuva', 'areasRisco', 'abrigos']} municipio={municipio} posicao={posicao} altura="h-[340px]" mostrarLegenda={false} />
          <Link to="/mapa" className="mt-2 inline-flex min-h-11 items-center gap-1 text-[15px] font-semibold text-oceano">Abrir o mapa completo <IcSeta tamanho={18} /></Link>
        </div>
      </div>

      <div className="grid content-start gap-4">
        <AvisosRegiao avisos={avisosAqui} municipio={municipio} doCache={avisos.doCache} />
        <AbrigosProximos dados={abrigos.dados} doCache={abrigos.doCache} posicao={posicao} municipio={municipio} />
        <Cartao titulo="Canais de emergência"><ContatosEmergencia compacto /></Cartao>
        <Cartao titulo="Prepare-se">
          <ul className="grid list-disc gap-1.5 pl-5 text-[15px]">
            {(ctx.dados?.orientacoes ?? ORIENTACOES).slice(0, 6).map((o) => <li key={o}>{o}</li>)}
          </ul>
          <Fonte nome="Orientações preventivas aprovadas, adaptadas de orientações públicas de Defesa Civil" />
          <LeitorVoz rotulo="Ouvir orientações" compacto texto={(ctx.dados?.orientacoes ?? ORIENTACOES).slice(0, 6).join('. ')} />
        </Cartao>
      </div>
    </div>
  )
}

/** Cada pedido compartilhado pode receber ofertas, inclusive quando é o único da região. */
function PedidosProximosInicio({ posicao, municipio }: { posicao: Posicao | null; municipio: string | null }) {
  const { dados } = usePedidosComunitarios(posicao, 5, posicao ? null : municipio)
  const [ofertando, setOfertando] = useState<string | null>(null)
  const [mensagem, setMensagem] = useState<string | null>(null)
  const pedidos = dados?.pedidos ?? []
  if ((!posicao && !municipio) || pedidos.length === 0) return null

  return (
    <section aria-labelledby="titulo-ajuda-proxima" className="grid gap-3 rounded-cartao bg-branco p-4 ring-1 ring-linha">
      <div>
        <h2 id="titulo-ajuda-proxima" className="text-[19px] font-bold text-petroleo">
          {pedidos.length === 1 ? '1 pedido de ajuda compartilhado na região' : `${pedidos.length} pedidos de ajuda compartilhados na região`}
        </h2>
        <p className="mt-1 text-[13px] text-tinta-suave">
          Apenas resumos autorizados pelas famílias, com localização aproximada. Ofertas são avaliadas pela equipe responsável.
        </p>
      </div>
      {pedidos.slice(0, 3).map((p) => (
        <div key={p.id} className="grid gap-2">
          <CartaoPedido p={p} aoOferecer={() => { setOfertando(p.id); setMensagem(null) }} />
          {ofertando === p.id && <FormOferta p={p} aoFechar={() => setOfertando(null)} aoEnviar={(texto) => { setMensagem(texto); setOfertando(null) }} />}
        </div>
      ))}
      {mensagem && <p role="status" className="rounded-controle bg-seguro-claro p-3 text-[14px] text-seguro">{mensagem}</p>}
      {pedidos.length > 3 && <Link to="/ajudar" className="min-h-11 rounded-controle bg-oceano-suave p-3 text-center font-semibold text-oceano">Ver todos os pedidos próximos</Link>}
    </section>
  )
}

const ORIENTACOES = [
  'Tenha à mão documentos, remédios de uso contínuo, lanterna e carregador em saco plástico.',
  'Combine com a família um ponto de encontro seguro e um contato de referência.',
  'Não atravesse ruas alagadas, a pé ou de carro.',
]

export function ClimaRisco({ ctx, doCache, temPosicao }: { ctx: Contexto | null; doCache: string | null; temPosicao: boolean }) {
  const e = ctx?.estacaoMaisProxima
  const fala = ctx ? [
    ...ctx.estados.map((x) => `${x.titulo}. ${x.detalhe}`),
    e ? `Chuva registrada na ${descreverEstacao(e)}, a ${formatarKm(e.distanciaKm)}: ${e.mm1h == null ? 'sem leitura na última hora' : `${String(e.mm1h).replace('.', ',')} milímetros na última hora`}, ${e.mm24h == null ? 'sem leitura em 24 horas' : `${String(e.mm24h).replace('.', ',')} milímetros em 24 horas`}${e.estado !== 'Atual' ? '. Leitura antiga' : ''}.` : '',
    ctx.aviso,
  ].filter(Boolean).join(' ') : ''
  return (
    <section className="rounded-cartao bg-branco p-4 ring-1 ring-linha sm:p-5" aria-labelledby="titulo-clima">
      <div className="flex items-start gap-3">
        <span className="grid size-14 shrink-0 place-items-center rounded-controle bg-oceano-suave text-oceano"><IcChuva tamanho={32} /></span>
        <div className="min-w-0 flex-1">
          <div className="flex items-center justify-between gap-2">
            <h2 id="titulo-clima" className="text-[19px] font-bold text-petroleo">Clima e risco na região</h2>
            {doCache && <Selo tom="atencao">Cópia salva</Selo>}
          </div>
          <p className="text-[13.5px] text-tinta-suave">Dados da APAC e do INMET</p>
          {!temPosicao ? <p className="mt-2 text-[15px]">Ative a localização para cruzarmos seu ponto com chuva medida, avisos e áreas documentadas.</p>
            : !ctx ? <p className="mt-2 text-[15px] text-tinta-suave">Consultando fontes…</p> : (
              <>
                {e ? (
                  <div className="mt-2">
                    <p className="text-[16px]"><strong>Chuva:</strong> <span className="numero-tabular">{e.mm1h == null ? 'sem leitura' : `${String(e.mm1h).replace('.', ',')} mm`}</span> na última hora · <span className="numero-tabular">{e.mm24h == null ? 'sem leitura' : `${String(e.mm24h).replace('.', ',')} mm`}</span> em 24 h</p>
                    <p className="text-[13.5px] text-tinta-suave">Medida na {descreverEstacao(e)}, a {formatarKm(e.distanciaKm)} · leitura {formatarHora(e.ultimaLeituraEm)}</p>
                    {e.estado !== 'Atual' && <p className="text-[13.5px] font-semibold text-atencao">Leitura antiga: não é informação em tempo real.</p>}
                    <p className="mt-1 text-[12.5px] text-tinta-suave">Chuva medida na estação não comprova alagamento no seu ponto.</p>
                  </div>
                ) : <p className="mt-2 text-[15px]">Sem estação com leitura recente a até 10 km.</p>}
                <ul className="mt-3 grid gap-2">
                  {ctx.estados.map((x, i) => (
                    <li key={i}>
                      <Selo tom={tomEstado[x.tipo] ?? 'neutro'}>{x.titulo}</Selo>
                      <p className="mt-1 text-[14.5px]">{x.detalhe}</p>
                      {x.fonte && <Fonte nome={x.fonte.nome} em={x.fonte.atualizadoEm} oficial={x.fonte.oficial} demonstrativa={x.fonte.demonstrativa} />}
                    </li>
                  ))}
                </ul>
                <p className="mt-3 text-[13px] font-semibold text-petroleo">{ctx.aviso}</p>
                <LeitorVoz rotulo="Ouvir situação da região" texto={fala} destaque />
              </>
            )}
        </div>
      </div>
      <Link to="/mapa" className="mt-3 flex min-h-11 items-center justify-center gap-1 rounded-controle bg-fundo text-[15px] font-semibold text-oceano">Ver no mapa <IcSeta tamanho={18} /></Link>
    </section>
  )
}

function AvisosRegiao({ avisos, municipio, doCache }: { avisos: Aviso[]; municipio: string | null; doCache: string | null }) {
  const oficial = avisos.some((a) => a.oficial)
  return (
    <section aria-labelledby="titulo-avisos" className={`rounded-cartao p-4 ring-1 sm:p-5 ${oficial ? 'border-l-[6px] border-emergencia bg-emergencia-claro/70 ring-emergencia/25' : 'bg-branco ring-linha'}`}>
      <div className="flex items-start gap-3">
        <span className={oficial ? 'text-emergencia' : 'text-tinta-suave'}><IcAlerta tamanho={30} /></span>
        <div className="min-w-0 flex-1">
          <div className="flex items-center justify-between gap-2">
            <h2 id="titulo-avisos" className="text-[19px] font-bold text-petroleo">Avisos para sua região</h2>
            {doCache && <Selo tom="atencao">Cópia salva {formatarRelativo(doCache)}</Selo>}
          </div>
          {avisos.length === 0 ? (
            <p className="mt-1 text-[15px]">Nenhum aviso vigente {municipio ? `para ${nomeMunicipio(municipio)}` : 'na RMR'} nas fontes consultadas (INMET). Isso não significa ausência de risco.</p>
          ) : (
            <ul className="mt-2 grid gap-3">
              {avisos.slice(0, 3).map((a) => (
                <li key={a.id}>
                  <p className="text-[15.5px]"><strong>{!a.oficial && !a.titulo.startsWith('[') ? '[Demonstração] ' : ''}{a.titulo}:</strong> {a.severidade}, até {formatarHora(a.fimEm)}.{a.instrucoes[0] ? ` ${a.instrucoes[0]}` : ''}</p>
                  <Fonte nome={a.fonteNome} oficial={a.oficial} demonstrativa={!a.oficial} />
                  <LeitorVoz compacto rotulo="Ouvir aviso" texto={`${a.oficial ? 'Aviso oficial' : 'Aviso demonstrativo, não oficial'}. ${a.titulo}. ${a.severidade}. ${a.instrucoes.slice(0, 3).join('. ')}`} />
                </li>
              ))}
            </ul>
          )}
          <Link to="/alertas" className="mt-2 inline-flex min-h-11 items-center gap-1 text-[15px] font-semibold text-oceano">Todos os alertas <IcSeta tamanho={18} /></Link>
        </div>
      </div>
    </section>
  )
}

export function AbrigosProximos({ dados, doCache, posicao, municipio }: { dados: RespostaAbrigos | null; doCache: string | null; posicao: { latitude: number; longitude: number; precisao: number | null; lidaEm: string } | null; municipio: string | null }) {
  const lista = abrigosProximos(dados?.abrigos ?? [], posicao, municipio)
  return (
    <section className="rounded-cartao bg-branco p-4 ring-1 ring-linha sm:p-5" aria-labelledby="titulo-abrigos">
      <div className="flex items-center justify-between gap-2">
        <h2 id="titulo-abrigos" className="flex items-center gap-2 text-[19px] font-bold text-petroleo"><span className="text-seguro-vivo"><IcAbrigo tamanho={28} /></span>Abrigos próximos</h2>
        <Link to="/abrigos" className="inline-flex min-h-11 items-center gap-1 text-[15px] font-semibold text-oceano">Ver todos <IcSeta tamanho={18} /></Link>
      </div>
      {lista.length === 0 ? (
        <p className="mt-2 rounded-controle bg-fundo p-3 text-[14.5px]">
          {dados?.fonte && !dados.fonte.disponivel && !dados.fonte.ultimoSucessoEm
            ? 'Não foi possível consultar a base de abrigos agora.'
            : 'Nenhum abrigo ativo publicado nas fontes consultadas neste momento.'}{' '}
          Abrigos são abertos pelas prefeituras durante emergências. Em caso de necessidade, ligue para a Defesa Civil (199).
        </p>
      ) : (
        <ul className="mt-1 divide-y divide-linha">
          {lista.map((a) => (
            <li key={a.id} className="flex items-start justify-between gap-3 py-3">
              <div className="min-w-0">
                <p className="text-[16px] font-bold text-petroleo">{a.nome}</p>
                <p className="text-[14px]">{a.endereco ?? 'Endereço não informado pela fonte'} · {nomeMunicipio(a.municipioCodigo)}</p>
                {a.km != null && <p className="flex items-center gap-1 text-[13.5px] text-tinta-suave"><IcLocal tamanho={15} />{formatarKm(a.km)} em linha reta</p>}
              </div>
              <span className="shrink-0 rounded-full bg-atencao-claro px-2.5 py-1 text-[12.5px] font-semibold text-atencao">{a.disponibilidade}</span>
            </li>
          ))}
        </ul>
      )}
      <p className="mt-1 text-[12.5px] text-tinta-suave">{dados?.observacao}{doCache ? ' Lista salva no aparelho.' : ''}</p>
    </section>
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
      <LeitorVoz texto={`${n.titulo}. ${n.mensagem} ${n.pergunta} Responda: Estou seguro, Preciso de socorro ou Ver orientações.`} rotulo="Ouvir aviso" compacto />
      <div className="mt-3 grid gap-2 sm:grid-cols-3">
        <Botao variante="seguro" onClick={async () => { await responder('EstouSeguro'); aoResponder(); navegar(`/seguro?aviso=${n.aviso?.id ?? ''}`) }}>Estou seguro</Botao>
        <Botao variante="perigo" onClick={async () => { await responder('PrecisoDeSocorro'); aoSocorro() }}>Preciso de socorro</Botao>
        <Botao variante="secundario" onClick={async () => { setOrientacoes(true); await responder('VerOrientacoes') }}>Ver orientações</Botao>
      </div>
      {orientacoes && n.aviso && (
        <>
          <ul className="mt-3 list-disc pl-5 text-[14.5px]">{n.aviso.instrucoes.map((i) => <li key={i}>{i}</li>)}</ul>
          <LeitorVoz texto={n.aviso.instrucoes.join('. ')} rotulo="Ouvir orientação" compacto />
        </>
      )}
    </section>
  )
}

