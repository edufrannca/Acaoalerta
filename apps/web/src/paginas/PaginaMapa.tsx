import { useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router'
import type { Map as MapaML } from 'maplibre-gl'
import { Mapa, ROTULOS_CAMADAS, type Camada } from '../componentes/Mapa'
import { LeitorVoz } from '../componentes/LeitorVoz'
import { CondicoesAlagamento } from '../componentes/CondicoesAlagamento'
import { Selo } from '../componentes/Base'
import { CartaoPedido, FormOferta, ResumoRegiao } from '../componentes/PedidosComunidade'
import { usePedidosComunitarios, type RespostaComunidade } from '../lib/comunidade'
import { IcAjudar, IcAbrigo, IcAlerta, IcCamadas, IcChuva, IcLocal, IcMais, IcMenos, IcRecentrar, IcSino, IcVoltar } from '../componentes/Icones'
import { formatarHora } from '../lib/api'
import { useDados } from '../lib/hooks'
import { MUNICIPIOS } from '../lib/municipios'
import { abrigosProximos, descreverEstacao, formatarKm, rotuloLocal, useRegiao, type Contexto, type RespostaAbrigos } from '../lib/regiao'

type Filtro = 'tudo' | 'pedidos' | 'areasRisco' | 'chuva' | 'avisos' | 'abrigos'
const CAMADAS: Record<Filtro, Camada[]> = {
  tudo: ['limites', 'avisos', 'chuva', 'areasRisco', 'abrigos', 'pedidos'],
  pedidos: ['limites', 'pedidos'],
  areasRisco: ['limites', 'areasRisco'],
  chuva: ['limites', 'chuva'],
  avisos: ['limites', 'avisos'],
  abrigos: ['limites', 'abrigos'],
}
const FILTROS: { id: Filtro; rotulo: string; Icone: (p: { tamanho?: number }) => React.ReactElement }[] = [
  { id: 'tudo', rotulo: 'Tudo', Icone: IcCamadas },
  { id: 'pedidos', rotulo: 'Pedidos de ajuda', Icone: IcAjudar },
  { id: 'areasRisco', rotulo: 'Áreas de risco', Icone: IcAlerta },
  { id: 'chuva', rotulo: 'Estações de chuva', Icone: IcChuva },
  { id: 'avisos', rotulo: 'Avisos', Icone: IcSino },
  { id: 'abrigos', rotulo: 'Abrigos', Icone: IcAbrigo },
]
const CORES: Record<Camada, string> = { limites: '#193B44', avisos: '#A83232', areasRisco: '#B87A16', chuva: '#0F686D', abrigos: '#2E7257', pedidos: '#B4472F' }

export function PaginaMapa() {
  const navegar = useNavigate()
  const { posicao, gps, iniciar, ctx, local } = useRegiao()
  const [filtro, setFiltro] = useState<Filtro>('tudo')
  const [municipio, setMunicipio] = useState('')
  const [expandido, setExpandido] = useState(false)
  const mapa = useRef<MapaML | null>(null)
  const abrigos = useDados<RespostaAbrigos>('/api/abrigos', { cache: true })
  const [raio, setRaio] = useState(5)
  const [selecionado, setSelecionado] = useState<string | null>(null)
  const comunidade = usePedidosComunitarios(posicao, raio, municipio || null)
  const rotulo = posicao ? rotuloLocal(local.dados, ctx.dados) ?? 'Identificando…' : null

  const recentrar = () => {
    if (!posicao) { iniciar(); return }
    mapa.current?.flyTo({ center: [posicao.longitude, posicao.latitude], zoom: 14.5, duration: 700 })
  }
  const zoom = (d: number) => mapa.current?.easeTo({ zoom: (mapa.current.getZoom() ?? 10) + d, duration: 250 })
  const btnMapa = 'grid size-12 place-items-center rounded-full bg-branco text-petroleo shadow-md ring-1 ring-linha hover:bg-fundo'

  const selecionar = (id: string) => {
    setSelecionado(id); setExpandido(true)
    setTimeout(() => document.querySelectorAll(`#pedido-${id}`).forEach((el) => el.scrollIntoView({ behavior: 'smooth', block: 'nearest' })), 120)
  }
  const pedidos = filtro === 'tudo' || filtro === 'pedidos'
    ? <Pedidos r={comunidade.dados} raio={raio} setRaio={setRaio} temGps={!!posicao} selecionado={selecionado} /> : null
  const painel = (
    <div className="grid gap-3">
      {filtro === 'pedidos' && pedidos}
      {(filtro === 'tudo' || filtro === 'chuva') && <CondicoesAlagamento compacto latitude={posicao?.latitude ?? null} longitude={posicao?.longitude ?? null} regiao={rotulo} />}
      <PainelInfo ctx={ctx.dados} doCache={ctx.doCache} abrigos={abrigos.dados} posicao={posicao} municipio={municipio || ctx.dados?.municipio?.codigo || null} filtro={filtro} gps={gps} iniciar={iniciar} />
      {filtro === 'tudo' && pedidos}
    </div>
  )

  return (
    <div className="relative h-[calc(100dvh-4rem-env(safe-area-inset-bottom))] md:mx-auto md:grid md:h-[calc(100dvh-5.5rem)] md:max-w-6xl md:grid-cols-[1fr_380px] md:gap-4 md:px-4 md:py-4">
      {/* Cabeçalho do mapa (mobile) */}
      <div className="absolute inset-x-0 top-0 z-20 bg-branco/95 px-3 pb-2 pt-[max(0.5rem,env(safe-area-inset-top))] shadow-sm md:static md:col-span-2 md:rounded-cartao md:px-4 md:py-3 md:shadow-none md:ring-1 md:ring-linha">
        <div className="flex items-center gap-2">
          <button type="button" onClick={() => navegar(-1)} className="grid size-11 place-items-center rounded-controle bg-fundo text-petroleo md:hidden" aria-label="Voltar"><IcVoltar /></button>
          <h1 className="flex-1 text-[21px] font-bold text-petroleo md:text-[24px]">Mapa da região</h1>
          <Link to="/alertas" className="grid size-11 place-items-center rounded-controle bg-fundo text-petroleo" aria-label="Alertas"><IcSino /></Link>
        </div>
        <label className="mt-2 flex min-h-11 items-center gap-2 rounded-controle border border-linha bg-branco px-3">
          <span className="text-seguro-vivo"><IcLocal tamanho={20} /></span>
          <span className="sr-only">Área exibida</span>
          <select value={municipio} onChange={(e) => setMunicipio(e.target.value)} className="min-h-10 flex-1 bg-transparent text-[16px] font-semibold text-tinta">
            <option value="">{rotulo ?? 'Toda a Região Metropolitana do Recife'}</option>
            {Object.entries(MUNICIPIOS).map(([c, n]) => <option key={c} value={c}>{n} - PE</option>)}
          </select>
        </label>
      </div>

      {/* Mapa */}
      <div className="absolute inset-0 md:relative md:inset-auto">
        <Mapa camadas={CAMADAS[filtro]} municipio={municipio || null} posicao={posicao} mostrarLegenda={false} controlesNativos={false}
          altura="h-full" classeExterna="h-full" classeContorno="md:rounded-cartao md:ring-1 md:ring-linha" aoPronto={(m) => { mapa.current = m }}
          pedidos={comunidade.dados?.pedidos} aoSelecionarPedido={selecionar} raioBuscaKm={filtro === 'tudo' || filtro === 'pedidos' ? raio : null} />
        {/* Filtros de camadas */}
        <div className="absolute inset-x-0 top-[8.6rem] z-10 flex gap-2 overflow-x-auto px-3 pb-1 md:top-3" role="radiogroup" aria-label="Camadas do mapa">
          {FILTROS.map(({ id, rotulo: r, Icone }) => (
            <button key={id} type="button" role="radio" aria-checked={filtro === id} onClick={() => setFiltro(id)}
              className={`flex min-h-11 shrink-0 items-center gap-1.5 rounded-full px-3.5 text-[14.5px] font-semibold shadow-md ring-1 ${filtro === id ? 'bg-petroleo text-branco ring-petroleo' : 'bg-branco text-petroleo ring-linha'}`}>
              <Icone tamanho={18} />{r}
            </button>
          ))}
        </div>
        {/* Controles */}
        <div className={`absolute right-3 z-10 grid gap-2 transition-[bottom] ${expandido ? 'bottom-[72%]' : 'bottom-[42%]'} md:bottom-6`}>
          <button type="button" className={btnMapa} onClick={recentrar} aria-label={posicao ? 'Centralizar na minha posição' : 'Ativar localização'}><IcRecentrar /></button>
          <div className="grid overflow-hidden rounded-full bg-branco shadow-md ring-1 ring-linha">
            <button type="button" className="grid size-12 place-items-center text-petroleo hover:bg-fundo" onClick={() => zoom(1)} aria-label="Aproximar"><IcMais /></button>
            <button type="button" className="grid size-12 place-items-center border-t border-linha text-petroleo hover:bg-fundo" onClick={() => zoom(-1)} aria-label="Afastar"><IcMenos /></button>
          </div>
        </div>
      </div>

      {/* Painel inferior (mobile) */}
      <section aria-label="Informações da região" className={`absolute inset-x-0 bottom-0 z-20 flex flex-col rounded-t-[22px] bg-branco shadow-[0_-6px_24px_rgba(16,40,45,0.18)] transition-[height] md:hidden ${expandido ? 'h-[70%]' : 'h-[40%]'}`}>
        <button type="button" onClick={() => setExpandido(!expandido)} aria-expanded={expandido} className="flex min-h-11 w-full flex-col items-center justify-center gap-1 pt-2"
          aria-label={expandido ? 'Recolher informações' : 'Expandir informações'}>
          <span className="h-1.5 w-12 rounded-full bg-linha" aria-hidden="true" />
          <span className="text-[12.5px] font-semibold text-tinta-suave">{expandido ? 'Recolher' : 'Mais informações'}</span>
        </button>
        <div className="flex-1 overflow-y-auto px-3 pb-4">{painel}</div>
      </section>

      {/* Painel lateral (desktop) */}
      <aside className="hidden overflow-y-auto md:block" aria-label="Informações da região">{painel}</aside>
    </div>
  )
}

function Pedidos({ r, raio, setRaio, temGps, selecionado }: { r: RespostaComunidade | null; raio: number; setRaio: (n: number) => void; temGps: boolean; selecionado: string | null }) {
  const [ofertando, setOfertando] = useState<string | null>(null)
  const [mensagem, setMensagem] = useState<string | null>(null)
  return (
    <section aria-labelledby="titulo-pedidos" className="grid gap-2.5">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <h2 id="titulo-pedidos" className="text-[18px] font-bold text-petroleo">Pedidos de ajuda {temGps ? 'perto de você' : 'na região'}</h2>
        {temGps && (
          <label className="text-[13.5px] font-medium">Raio de busca{' '}
            <select value={raio} onChange={(e) => setRaio(Number(e.target.value))} className="min-h-10 rounded-controle border border-linha bg-branco px-2">
              {[2, 5, 10, 20].map((k) => <option key={k} value={k}>{k} km</option>)}
            </select>
          </label>
        )}
      </div>
      <p className="text-[13px] text-tinta-suave">{temGps ? 'Busca centrada na sua localização atual (linha azul tracejada).' : 'Ative a localização para buscar a partir de onde você está.'} {r?.areaAproximada}</p>
      {r && <ResumoRegiao r={r} />}
      {r?.pedidos.map((p) => (
        <div key={p.id} className="grid gap-2">
          <CartaoPedido p={p} destaque={p.id === selecionado} aoOferecer={() => { setOfertando(p.id); setMensagem(null) }} />
          {ofertando === p.id && <FormOferta p={p} aoFechar={() => setOfertando(null)} aoEnviar={(texto) => { setMensagem(texto); setOfertando(null) }} />}
        </div>
      ))}
      {mensagem && <p role="status" className="rounded-controle bg-seguro-claro p-3 text-[14px] text-seguro">{mensagem}</p>}
      {r && r.pedidos.length > 0 && <Link to="/ajudar" className="min-h-11 rounded-controle bg-oceano-suave px-3 py-2.5 text-center text-[15px] font-semibold text-oceano">Ver todos os pedidos e minhas ofertas</Link>}
      {r?.demonstrativos && <p className="text-[12.5px] text-tinta-suave">Área de demonstração: exibindo pedidos FICTÍCIOS.</p>}
    </section>
  )
}

function Linha({ icone, tom, titulo, valor, detalhe, para }: { icone: React.ReactNode; tom: string; titulo: string; valor: string; detalhe?: React.ReactNode; para?: string }) {
  const conteudo = (
    <div className="flex items-center gap-3 rounded-cartao bg-branco p-3 ring-1 ring-linha">
      <span className={`grid size-14 shrink-0 place-items-center rounded-controle ${tom}`}>{icone}</span>
      <span className="min-w-0 flex-1">
        <span className="block text-[13.5px] font-semibold text-tinta-suave">{titulo}</span>
        <span className="block text-[19px] font-bold leading-tight text-petroleo">{valor}</span>
        {detalhe && <span className="block text-[13.5px] text-tinta-suave">{detalhe}</span>}
      </span>
    </div>
  )
  return para ? <Link to={para}>{conteudo}</Link> : conteudo
}

function PainelInfo({ ctx, doCache, abrigos, posicao, municipio, filtro, gps, iniciar }: {
  ctx: Contexto | null; doCache: string | null; abrigos: RespostaAbrigos | null; posicao: { latitude: number; longitude: number; precisao: number | null; lidaEm: string } | null
  municipio: string | null; filtro: Filtro; gps: string; iniciar: () => void
}) {
  const e = ctx?.estacaoMaisProxima
  const prox = abrigosProximos(abrigos?.abrigos ?? [], posicao, municipio, 1)[0]
  const principal = ctx?.estados.find((x) => x.tipo === 'AvisoOficialVigente') ?? ctx?.estados.find((x) => x.tipo === 'SuscetibilidadeConhecida') ?? ctx?.estados[0]
  const fala = ctx ? [
    ...ctx.estados.map((x) => `${x.titulo}. ${x.detalhe}`),
    e ? `Chuva na ${descreverEstacao(e)}, a ${formatarKm(e.distanciaKm)}: ${e.mm1h == null ? 'sem leitura na última hora' : `${String(e.mm1h).replace('.', ',')} milímetros na última hora`}.` : '',
    prox ? `Abrigo ativo mais próximo: ${prox.nome}${prox.km != null ? `, a ${formatarKm(prox.km)} em linha reta` : ''}. Disponibilidade não confirmada.` : 'Nenhum abrigo ativo publicado agora.',
    ctx.aviso,
  ].filter(Boolean).join(' ') : ''

  return (
    <div className="grid gap-2.5 md:gap-3">
      {!posicao && (
        <div className="rounded-cartao bg-oceano-suave p-3 text-[14.5px]">
          {gps === 'negado' ? 'Permissão de localização negada. Escolha o município acima para explorar o mapa.' : 'Ative a localização para ver a situação conhecida no seu ponto.'}
          {gps !== 'negado' && gps !== 'solicitando' && <button type="button" onClick={iniciar} className="ml-2 min-h-11 font-semibold text-oceano underline">Usar minha localização</button>}
        </div>
      )}
      {posicao && ctx && (
        <>
          <Linha icone={<IcAlerta tamanho={30} />} tom={principal?.tipo === 'AvisoOficialVigente' ? 'bg-emergencia-claro text-emergencia' : principal?.tipo === 'SuscetibilidadeConhecida' ? 'bg-atencao-claro text-atencao' : 'bg-fundo text-tinta-suave'}
            titulo="Situação conhecida" valor={principal?.titulo ?? 'Condição desconhecida'} detalhe={principal?.detalhe} />
          <Linha icone={<IcChuva tamanho={30} />} tom="bg-oceano-suave text-oceano"
            titulo={e ? `Chuva (${e.rede ? `estação ${e.rede}` : 'estação'} «${e.nomeEstacao ?? e.nome}»)` : 'Chuva'}
            valor={e ? (e.mm1h == null ? 'Sem leitura' : `${String(e.mm1h).replace('.', ',')} mm na última hora`) : 'Sem estação próxima'}
            detalhe={e ? <>{e.mm24h == null ? '' : `${String(e.mm24h).replace('.', ',')} mm em 24 h · `}a {formatarKm(e.distanciaKm)} · leitura {formatarHora(e.ultimaLeituraEm)}{e.estado !== 'Atual' ? ' · ANTIGA' : ''}</> : 'Nenhuma leitura recente a até 10 km'} />
        </>
      )}
      <Linha icone={<IcAbrigo tamanho={30} />} tom="bg-seguro-claro text-seguro" para="/abrigos"
        titulo="Abrigo ativo mais próximo" valor={prox?.nome ?? 'Nenhum abrigo ativo publicado'}
        detalhe={prox ? <>{prox.km != null ? `≈ ${formatarKm(prox.km)} em linha reta · ` : ''}Disponibilidade não confirmada</> : 'Fonte: COP Recife (abrigos ativos). Em necessidade, ligue 199.'} />
      {ctx && <LeitorVoz rotulo="Ouvir situação da região" texto={fala} destaque compacto />}

      <div className="rounded-cartao bg-fundo p-3 text-[13px] text-tinta-suave">
        <p className="mb-1.5 font-semibold text-tinta">Legenda {doCache && <Selo tom="atencao">cópia salva</Selo>}</p>
        <ul className="grid gap-1">
          {CAMADAS[filtro].filter((c) => c !== 'limites').map((c) => (
            <li key={c} className="flex items-start gap-2"><span className="mt-1 inline-block size-3 shrink-0 rounded-full" style={{ background: CORES[c] }} aria-hidden="true" />{ROTULOS_CAMADAS[c]}</li>
          ))}
          {posicao && <li className="flex items-start gap-2"><span className="mt-1 inline-block size-3 rounded-full bg-[#2F6FD6]" aria-hidden="true" />Sua posição (GPS, ±{posicao.precisao ?? '?'} m, {formatarHora(posicao.lidaEm)})</li>}
        </ul>
        <p className="mt-2">Fontes: APAC/SIRH (chuva), INMET (avisos), Defesa Civil do Recife (áreas documentadas, Regional Sul), COP Recife (abrigos ativos), limites oficiais APAC, mapa base © OpenStreetMap. Não traçamos rotas: confirme o caminho com a Defesa Civil. Ausência de marcação não significa ausência de risco.</p>
      </div>
    </div>
  )
}
