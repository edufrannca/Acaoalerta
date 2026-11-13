import { useDados, useLocalizacao, type Posicao } from './hooks'

export interface EstadoRegiao { tipo: string; titulo: string; detalhe: string; fonte: { nome: string; atualizadoEm: string | null; oficial: boolean; demonstrativa: boolean } | null }
export interface Estacao {
  nome: string; nomeOficial: string; nomeEstacao: string; rede: string | null; municipio: string
  ultimaLeituraEm: string | null; mm1h: number | null; mm24h: number | null; distanciaKm: number; estado: string; fonte: string
}
export interface Contexto {
  municipio: { codigo: string; nome: string } | null
  foraDaRmr: boolean
  estados: EstadoRegiao[]
  orientacoes: string[]
  limitacoes: string[]
  estacaoMaisProxima: Estacao | null
  avaliadoEm: string
  aviso: string
}
export interface Localidade {
  localidade: string | null
  precisao: 'bairro' | 'municipio' | 'coordenadas'
  municipio: { codigo: string; nome: string } | null
  uf: string | null
  foraDaRmr: boolean
  fonteMunicipio: string
  fonteLocalidade: string | null
  observacao: string | null
}
export interface Abrigo {
  id: string; nome: string; tipo: string | null; endereco: string | null; latitude: number; longitude: number; ficticio: boolean
  disponibilidade: string; fonte: string; fonteUrl: string | null; municipioCodigo: string; acessibilidade: string
  statusFonte: string | null; estagioOperacional: string | null; atualizadoNaFonteEm: string | null; obtidoEm: string
}
export interface RespostaAbrigos {
  abrigos: Abrigo[]
  observacao: string
  cobertura: string
  fonte: { nome: string; disponivel: boolean; ultimoSucessoEm: string | null; ultimaConsultaEm: string | null; ultimoErro: string | null } | null
}

/** Distância em km (Haversine). */
export function distanciaKm(a: { latitude: number; longitude: number }, b: { latitude: number; longitude: number }) {
  const r = (g: number) => (g * Math.PI) / 180
  const dLat = r(b.latitude - a.latitude), dLon = r(b.longitude - a.longitude)
  const h = Math.sin(dLat / 2) ** 2 + Math.cos(r(a.latitude)) * Math.cos(r(b.latitude)) * Math.sin(dLon / 2) ** 2
  return 6371 * 2 * Math.asin(Math.sqrt(h))
}

export const formatarKm = (km: number) => (km < 1 ? `${Math.round(km * 1000 / 10) * 10} m` : `${km.toFixed(1).replace('.', ',')} km`)

/**
 * Localização do aparelho + contexto territorial + localidade.
 * A chave usa coordenadas arredondadas (~110 m): o contexto é reavaliado quando a pessoa se desloca de forma relevante,
 * sem uma consulta a cada leitura do GPS.
 */
export function useRegiao() {
  const { posicao, estado: gps, iniciar } = useLocalizacao()
  const q = posicao ? `lat=${posicao.latitude.toFixed(3)}&lon=${posicao.longitude.toFixed(3)}` : null
  const ctx = useDados<Contexto>(q ? `/api/territorio/contexto?${q}` : null, { cache: true, intervaloMs: 5 * 60 * 1000 })
  const local = useDados<Localidade>(q ? `/api/territorio/localidade?${q}` : null, { cache: true })
  return { posicao, gps, iniciar, ctx, local }
}

/** "Janga, Paulista - PE" quando o bairro foi confirmado; senão "Paulista - PE". Nunca usa nome de estação. */
export function rotuloLocal(l: Localidade | null, ctx: Contexto | null): string | null {
  const mun = l?.municipio?.nome ?? ctx?.municipio?.nome
  if (l?.foraDaRmr || ctx?.foraDaRmr) return 'Fora da Região Metropolitana do Recife'
  if (!mun) return null
  return l?.localidade ? `${l.localidade}, ${mun} - PE` : `${mun} - PE`
}

/** Rede e nome oficial da estação, apresentados como tal (ex.: "estação APAC «Janga 2»"). */
export function descreverEstacao(e: Estacao): string {
  const nome = e.nomeEstacao ?? e.nome
  return `${e.rede ? `estação ${e.rede}` : 'estação'} «${nome}», ${e.municipio}`
}

export function abrigosProximos(lista: Abrigo[], posicao: Posicao | null, municipio: string | null, n = 3) {
  const base = posicao ? lista : municipio ? lista.filter((a) => a.municipioCodigo === municipio) : lista
  return base.map((a) => ({ ...a, km: posicao ? distanciaKm(posicao, a) : null }))
    .sort((a, b) => (a.km ?? 0) - (b.km ?? 0)).slice(0, n)
}
