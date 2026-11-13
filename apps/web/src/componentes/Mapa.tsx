import * as maplibregl from 'maplibre-gl'
import type { GeoJSONSource, Map as MapaML, MapMouseEvent, MapGeoJSONFeature } from 'maplibre-gl'
import type * as GeoJSON from 'geojson'
import urlTrabalhador from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'

maplibregl.setWorkerUrl(urlTrabalhador)
import { useEffect, useMemo, useRef, useState } from 'react'
import { formatarHora } from '../lib/api'
import { useDados, type Posicao } from '../lib/hooks'
import { MUNICIPIOS } from '../lib/municipios'

export type Camada = 'limites' | 'avisos' | 'areasRisco' | 'chuva' | 'abrigos' | 'relatos'

export const ROTULOS_CAMADAS: Record<Camada, string> = {
  limites: 'Limites municipais',
  avisos: 'Avisos vigentes',
  areasRisco: 'Áreas de risco documentadas',
  chuva: 'Chuva acumulada (24 h)',
  abrigos: 'Abrigos e pontos de apoio',
  relatos: 'Relatos da comunidade (aproximados)',
}

const CORES: Record<Camada, string> = {
  limites: '#193B44', avisos: '#A83232', areasRisco: '#B87A16', chuva: '#0F686D', abrigos: '#2E7257', relatos: '#6E5A95',
}

export interface PontoOperador { id: string; latitude: number; longitude: number; nivel?: string | null; rotulo: string }

interface Props {
  camadas: Camada[]
  municipio?: string | null
  posicao?: Posicao | null
  pontosOperador?: PontoOperador[]
  aoSelecionarPonto?: (id: string) => void
  altura?: string
  mostrarLegenda?: boolean
  /** false: sem os botões nativos de zoom (a tela fornece os próprios controles). */
  controlesNativos?: boolean
  aoPronto?: (m: MapaML) => void
  classeContorno?: string
  classeExterna?: string
}

type FC = GeoJSON.FeatureCollection
const vazio: FC = { type: 'FeatureCollection', features: [] }
const pt = (lon: number, lat: number, props: Record<string, unknown>): GeoJSON.Feature => ({ type: 'Feature', geometry: { type: 'Point', coordinates: [lon, lat] }, properties: props })

interface Chuva { disponivel: boolean; consultadoEm: string; fonte: string; estacoes: { nome: string; nomeEstacao?: string; rede?: string | null; latitude: number; longitude: number; mm1h: number | null; mm24h: number | null; ultimaLeituraEm: string | null; estado: string; municipio: string }[] }
interface Abrigos { abrigos: { id: string; nome: string; endereco: string | null; latitude: number; longitude: number; ficticio: boolean; disponibilidade: string; fonte: string; municipioCodigo: string; acessibilidade: string; obtidoEm: string; statusFonte: string | null; atualizadoNaFonteEm: string | null }[] }
interface Areas { areas: { latitude: number; longitude: number; descricao: string; bairro: string | null; localidade: string | null; fonte: string }[]; fonte?: { ultimoSucessoEm?: string } | null }
interface Relatos { celulas: { latitude: number; longitude: number; relatos: number; verificadas: number; municipio: string }[]; atualizadoEm: string }
interface Aviso { id: string; titulo: string; oficial: boolean; severidade: string; fimEm: string; municipios: string[]; poligono: GeoJSON.Geometry | null; fonteNome: string }

const estilo: maplibregl.StyleSpecification = {
  version: 8,
  sources: {
    osm: { type: 'raster', tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'], tileSize: 256, maxzoom: 19, attribution: '© colaboradores do OpenStreetMap' },
  },
  layers: [
    { id: 'fundo', type: 'background', paint: { 'background-color': '#E9EEEC' } },
    { id: 'osm', type: 'raster', source: 'osm', paint: { 'raster-saturation': -0.55, 'raster-opacity': 0.9 } },
  ],
}

export function Mapa({ camadas, municipio, posicao, pontosOperador, aoSelecionarPonto, altura = 'h-[360px]', mostrarLegenda = true, controlesNativos = true, aoPronto, classeContorno = 'rounded-cartao ring-1 ring-linha', classeExterna = '' }: Props) {
  const div = useRef<HTMLDivElement>(null)
  const mapa = useRef<MapaML | null>(null)
  const [pronto, setPronto] = useState(false)
  const centralizou = useRef(false)
  const tem = (c: Camada) => camadas.includes(c)

  const limites = useDados<FC>(tem('limites') ? '/api/municipios/limites' : null, { cache: true })
  const avisos = useDados<Aviso[]>(tem('avisos') ? '/api/alertas/vigentes' : null, { cache: true, intervaloMs: 60000 })
  const areas = useDados<Areas>(tem('areasRisco') ? '/api/territorio/areas-risco' : null, { cache: true })
  const chuva = useDados<Chuva>(tem('chuva') ? '/api/monitoramento/chuva' : null, { cache: true, intervaloMs: 300000 })
  const abrigos = useDados<Abrigos>(tem('abrigos') ? '/api/abrigos' : null, { cache: true })
  const relatos = useDados<Relatos>(tem('relatos') ? '/api/ocorrencias/comunitarias' : null, { intervaloMs: 30000 })

  useEffect(() => {
    if (!div.current) return
    const m = new maplibregl.Map({ container: div.current, style: estilo, center: [-34.95, -8.05], zoom: 9.2, attributionControl: { compact: true }, cooperativeGestures: false })
    if (controlesNativos) m.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-right')
    m.addControl(new maplibregl.ScaleControl({ unit: 'metric' }), 'bottom-left')
    m.on('load', () => {
      const fontes = ['limites', 'avisos', 'areasRisco', 'chuva', 'abrigos', 'relatos', 'posicao', 'operador'] as const
      fontes.forEach((f) => m.addSource(f, { type: 'geojson', data: vazio }))
      m.addLayer({ id: 'limites-preenchimento', type: 'fill', source: 'limites', paint: { 'fill-color': ['case', ['boolean', ['get', 'selecionado'], false], '#0F686D', '#193B44'], 'fill-opacity': ['case', ['boolean', ['get', 'selecionado'], false], 0.1, 0.03] } })
      m.addLayer({ id: 'limites-linha', type: 'line', source: 'limites', paint: { 'line-color': '#193B44', 'line-width': ['case', ['boolean', ['get', 'selecionado'], false], 2.4, 1.1], 'line-opacity': 0.7 } })
      m.addLayer({ id: 'avisos-preenchimento', type: 'fill', source: 'avisos', paint: { 'fill-color': ['case', ['get', 'oficial'], '#A83232', '#6E5A95'], 'fill-opacity': 0.14 } })
      m.addLayer({ id: 'avisos-linha', type: 'line', source: 'avisos', paint: { 'line-color': ['case', ['get', 'oficial'], '#A83232', '#6E5A95'], 'line-width': 2, 'line-dasharray': [2, 1.5] } })
      m.addLayer({ id: 'areasRisco', type: 'circle', source: 'areasRisco', paint: { 'circle-radius': ['interpolate', ['linear'], ['zoom'], 10, 2, 15, 6], 'circle-color': CORES.areasRisco, 'circle-opacity': 0.75, 'circle-stroke-width': 0.5, 'circle-stroke-color': '#fff' } })
      m.addLayer({ id: 'chuva', type: 'circle', source: 'chuva', paint: {
        'circle-radius': ['interpolate', ['linear'], ['coalesce', ['get', 'mm'], 0], 0, 5, 20, 9, 60, 15, 120, 20],
        'circle-color': ['case', ['==', ['get', 'estado'], 'Atual'], CORES.chuva, '#9AA7A9'], 'circle-opacity': 0.55, 'circle-stroke-width': 1.5, 'circle-stroke-color': ['case', ['==', ['get', 'estado'], 'Atual'], CORES.chuva, '#7C8A8C'] } })
      m.addLayer({ id: 'abrigos', type: 'circle', source: 'abrigos', paint: { 'circle-radius': 7, 'circle-color': ['case', ['get', 'ficticio'], '#ffffff', CORES.abrigos], 'circle-stroke-width': 2.5, 'circle-stroke-color': CORES.abrigos } })
      m.addLayer({ id: 'relatos', type: 'circle', source: 'relatos', paint: { 'circle-radius': ['interpolate', ['linear'], ['get', 'relatos'], 1, 10, 10, 22], 'circle-color': CORES.relatos, 'circle-opacity': 0.28, 'circle-stroke-width': 1.5, 'circle-stroke-color': CORES.relatos } })
      m.addLayer({ id: 'operador', type: 'circle', source: 'operador', paint: { 'circle-radius': 9, 'circle-color': ['match', ['get', 'nivel'], 'IndicioAmeacaImediata', '#A83232', 'Atencao', '#B87A16', '#0F686D'], 'circle-stroke-width': 3, 'circle-stroke-color': '#fff' } })
      m.addLayer({ id: 'posicao-halo', type: 'circle', source: 'posicao', paint: { 'circle-radius': 16, 'circle-color': '#2F6FD6', 'circle-opacity': 0.18 } })
      m.addLayer({ id: 'posicao', type: 'circle', source: 'posicao', paint: { 'circle-radius': 7, 'circle-color': '#2F6FD6', 'circle-stroke-width': 3, 'circle-stroke-color': '#fff' } })

      const popup = (camada: string, html: (p: Record<string, unknown>) => string) => {
        m.on('click', camada, (e: MapMouseEvent & { features?: MapGeoJSONFeature[] }) => {
          const f = e.features?.[0]
          if (!f) return
          if (camada === 'operador' && aoSelecionarPonto) { aoSelecionarPonto(String(f.properties?.id)); return }
          new maplibregl.Popup({ maxWidth: '280px' }).setLngLat(e.lngLat).setHTML(html(f.properties ?? {})).addTo(m)
        })
        m.on('mouseenter', camada, () => { m.getCanvas().style.cursor = 'pointer' })
        m.on('mouseleave', camada, () => { m.getCanvas().style.cursor = '' })
      }
      const esc = (s: unknown) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]!)
      popup('chuva', (p) => `<strong>Estação ${esc(p.rede ?? '')} «${esc(p.nomeEstacao)}»</strong><br>${esc(p.municipio)}<br>${p.mm1h == null ? 'Sem leitura na última hora' : `${esc(p.mm1h)} mm na última hora`} · ${p.mm == null ? 'sem leitura em 24 h' : `${esc(p.mm)} mm em 24 h`}<br><small>${p.estado === 'Atual' ? 'Leitura' : 'Leitura antiga'}: ${esc(p.em)} — APAC/SIRH. Nome técnico da estação; não indica bairro.</small>`)
      popup('abrigos', (p) => `<strong>${esc(p.nome)}</strong><br>${esc(p.endereco ?? '')}<br>${esc(p.disponibilidade)}<br><small>Status publicado: ${esc(p.status ?? 'não informado')} · Acessibilidade: ${esc(p.acessibilidade)}<br>Fonte: ${esc(p.fonte)}${p.atualizado ? `, atualizado ${esc(p.atualizado)}` : ''}</small>`)
      popup('areasRisco', (p) => `<strong>${esc(p.descricao)}</strong><br>${esc(p.local)}<br><small>Fonte: ${esc(p.fonte)}</small>`)
      popup('relatos', (p) => `<strong>${esc(p.relatos)} relato(s) na área</strong><br>${esc(p.verificadas)} verificado(s)<br><small>Localização aproximada (~1 km). Relatos não verificados podem conter erros.</small>`)
      popup('avisos-preenchimento', (p) => `<strong>${esc(p.titulo)}</strong><br>${esc(p.severidade)} — até ${esc(p.fim)}<br><small>${p.oficial ? 'Aviso oficial' : 'AVISO DEMONSTRATIVO (não oficial)'} · ${esc(p.fonte)}</small>`)
      popup('operador', () => '')
      setPronto(true)
      aoPronto?.(m)
    })
    mapa.current = m
    return () => { m.remove(); mapa.current = null }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const set = (fonte: string, dados: FC) => (mapa.current?.getSource(fonte) as GeoJSONSource | undefined)?.setData(dados)

  useEffect(() => {
    if (!pronto || !limites.dados) return
    set('limites', { ...limites.dados, features: limites.dados.features.map((f: GeoJSON.Feature) => ({ ...f, properties: { ...f.properties, selecionado: f.properties?.codigo === municipio } })) })
    if (municipio && !posicao) {
      const f = limites.dados.features.find((x: GeoJSON.Feature) => x.properties?.codigo === municipio)
      if (f) {
        const b = new maplibregl.LngLatBounds()
        const coords = (f.geometry.type === 'Polygon' ? [f.geometry.coordinates] : f.geometry.type === 'MultiPolygon' ? f.geometry.coordinates : []) as number[][][][]
        coords.flat(2).forEach((c) => b.extend(c as [number, number]))
        mapa.current?.fitBounds(b, { padding: 30, duration: 600 })
      }
    }
  }, [pronto, limites.dados, municipio, posicao])

  useEffect(() => {
    if (!pronto) return
    const lista = avisos.dados ?? []
    set('avisos', { type: 'FeatureCollection', features: lista.filter((a) => a.poligono).map((a) => ({ type: 'Feature', geometry: a.poligono!, properties: { titulo: a.titulo, oficial: a.oficial, severidade: a.severidade, fim: formatarHora(a.fimEm), fonte: a.fonteNome } })) })
  }, [pronto, avisos.dados])

  const filtrar = <T extends { municipioCodigo?: string; municipio?: string }>(l: T[]) => (municipio ? l.filter((x) => (x.municipioCodigo ?? x.municipio) === municipio || x.municipio === MUNICIPIOS[municipio]) : l)

  useEffect(() => { if (pronto) set('areasRisco', { type: 'FeatureCollection', features: (areas.dados?.areas ?? []).map((a) => pt(a.longitude, a.latitude, { descricao: a.descricao, local: [a.localidade, a.bairro].filter(Boolean).join(', '), fonte: a.fonte })) }) }, [pronto, areas.dados])
  useEffect(() => { if (pronto) set('chuva', { type: 'FeatureCollection', features: filtrar(chuva.dados?.estacoes ?? []).map((e) => pt(e.longitude, e.latitude, { nome: e.nome, nomeEstacao: e.nomeEstacao ?? e.nome, rede: e.rede ?? '', municipio: e.municipio, mm: e.mm24h, mm1h: e.mm1h, estado: e.estado, em: formatarHora(e.ultimaLeituraEm) })) }) }, [pronto, chuva.dados, municipio]) // eslint-disable-line
  useEffect(() => { if (pronto) set('abrigos', { type: 'FeatureCollection', features: filtrar(abrigos.dados?.abrigos ?? []).map((a) => pt(a.longitude, a.latitude, { nome: a.nome, endereco: a.endereco, ficticio: a.ficticio, disponibilidade: a.disponibilidade, fonte: a.fonte, acessibilidade: a.acessibilidade, status: a.statusFonte, atualizado: a.atualizadoNaFonteEm ? formatarHora(a.atualizadoNaFonteEm) : '' })) }) }, [pronto, abrigos.dados, municipio]) // eslint-disable-line
  useEffect(() => { if (pronto) set('relatos', { type: 'FeatureCollection', features: filtrar(relatos.dados?.celulas ?? []).map((c) => pt(c.longitude, c.latitude, { relatos: c.relatos, verificadas: c.verificadas })) }) }, [pronto, relatos.dados, municipio]) // eslint-disable-line
  const enquadrou = useRef(false)
  useEffect(() => {
    if (!pronto) return
    const lista = pontosOperador ?? []
    set('operador', { type: 'FeatureCollection', features: lista.map((p) => pt(p.longitude, p.latitude, { id: p.id, nivel: p.nivel ?? '', rotulo: p.rotulo })) })
    if (lista.length > 0 && !enquadrou.current) {
      enquadrou.current = true
      const b = new maplibregl.LngLatBounds()
      lista.forEach((p) => b.extend([p.longitude, p.latitude]))
      mapa.current?.fitBounds(b, { padding: 60, maxZoom: 14, duration: 600 })
    }
  }, [pronto, pontosOperador])

  useEffect(() => {
    if (!pronto) return
    set('posicao', posicao ? { type: 'FeatureCollection', features: [pt(posicao.longitude, posicao.latitude, {})] } : vazio)
    if (posicao && !centralizou.current) {
      centralizou.current = true
      mapa.current?.flyTo({ center: [posicao.longitude, posicao.latitude], zoom: 13.5, duration: 800 })
    }
  }, [pronto, posicao])

  useEffect(() => {
    if (!pronto || !mapa.current) return
    const vis = (id: string, c: Camada) => mapa.current!.getLayer(id) && mapa.current!.setLayoutProperty(id, 'visibility', tem(c) ? 'visible' : 'none')
    vis('limites-preenchimento', 'limites'); vis('limites-linha', 'limites')
    vis('avisos-preenchimento', 'avisos'); vis('avisos-linha', 'avisos')
    vis('areasRisco', 'areasRisco'); vis('chuva', 'chuva'); vis('abrigos', 'abrigos'); vis('relatos', 'relatos')
  }, [pronto, camadas]) // eslint-disable-line

  const legenda = useMemo(() => {
    const itens: { c: Camada; texto: string }[] = []
    if (tem('chuva')) itens.push({ c: 'chuva', texto: chuva.dados ? `APAC/SIRH, consultado ${formatarHora(chuva.dados.consultadoEm)}${chuva.dados.disponivel ? '' : ' (fonte indisponível agora)'}${chuva.doCache ? ' (cópia salva no aparelho)' : ''}` : chuva.erro ?? 'carregando' })
    if (tem('areasRisco')) itens.push({ c: 'areasRisco', texto: `Defesa Civil do Recife (Regional Sul), ${areas.dados?.areas.length ?? 0} pontos` })
    if (tem('abrigos')) itens.push({ c: 'abrigos', texto: `COP Recife, somente abrigos ativos publicados (${abrigos.dados?.abrigos.length ?? 0} agora)` })
    if (tem('relatos')) itens.push({ c: 'relatos', texto: relatos.dados ? `últimas 6 h, atualizado ${formatarHora(relatos.dados.atualizadoEm)}` : 'indisponível' })
    if (tem('avisos')) itens.push({ c: 'avisos', texto: 'vermelho = oficial (INMET), roxo = demonstrativo' })
    return itens
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [camadas, chuva.dados, chuva.erro, chuva.doCache, areas.dados, relatos.dados, abrigos.dados])

  return (
    <div className={classeExterna}>
      <div ref={div} className={`${altura} w-full overflow-hidden ${classeContorno}`} role="region" aria-label="Mapa interativo da Região Metropolitana do Recife" />
      {mostrarLegenda && legenda.length > 0 && (
        <ul className="mt-2 grid gap-1 text-[12.5px] text-tinta-suave sm:grid-cols-2">
          {legenda.map((l) => (
            <li key={l.c} className="flex items-start gap-2">
              <span className="mt-1 inline-block size-2.5 shrink-0 rounded-full" style={{ background: CORES[l.c] }} aria-hidden="true" />
              <span><span className="font-medium text-tinta">{ROTULOS_CAMADAS[l.c]}</span>: {l.texto}</span>
            </li>
          ))}
          {posicao && <li className="flex items-start gap-2"><span className="mt-1 inline-block size-2.5 rounded-full bg-[#2F6FD6]" aria-hidden="true" /><span><span className="font-medium text-tinta">Sua posição</span>: GPS do aparelho, {formatarHora(posicao.lidaEm)} (±{posicao.precisao ?? '?'} m)</span></li>}
        </ul>
      )}
    </div>
  )
}
