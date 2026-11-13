import { bd, dispositivoId } from './dispositivo'
import { useDados, type Posicao } from './hooks'

export interface Compartilhamento { autorizado: boolean; incluirPessoas: boolean; incluirNecessidades: boolean }

export const NECESSIDADES: { codigo: string; rotulo: string }[] = [
  { codigo: 'agua', rotulo: 'Água potável' },
  { codigo: 'alimentos', rotulo: 'Alimentos' },
  { codigo: 'abrigo', rotulo: 'Abrigo' },
  { codigo: 'transporte_assistido', rotulo: 'Transporte assistido' },
  { codigo: 'itens_higiene', rotulo: 'Itens de higiene' },
  { codigo: 'roupas', rotulo: 'Roupas e cobertas' },
  { codigo: 'outros', rotulo: 'Outros recursos' },
]

export interface PedidoComunitario {
  id: string
  referencia: string
  tipos: string[]
  pessoas: number | null
  criancas: string | null
  idosos: string | null
  mobilidadeReduzida: string | null
  necessidades: { codigo: string; rotulo: string }[] | null
  horarioAproximado: string
  situacao: string
  verificacao: string
  municipio: string | null
  area: { latitude: number; longitude: number; raioMetros: number }
  distanciaKm: number | null
  ofertasEmAndamento: number
  podeOferecerAjuda: boolean
  demonstrativo: boolean
}

export interface RespostaComunidade {
  atualizadoEm: string
  busca: { raioKm: number } | null
  demonstrativos: boolean
  pedidos: PedidoComunitario[]
  naoCompartilhados: number
  agregados: null | {
    pedidos: number; pessoasInformadas: number; pedidosComCriancas: number; pedidosComIdosos: number; pedidosComMobilidadeReduzida: number
    necessidades: { codigo: string; rotulo: string; pedidos: number }[]
  }
  agregadosOmitidos: string | null
  privacidade: string
  areaAproximada: string
  seguranca: string
  ambiente: string
}

/** Na área de demonstração (código da equipe informado), a lista mostra os pedidos FICTÍCIOS em vez dos reais. */
export function emDemonstracao(): boolean {
  try { return !!sessionStorage.getItem('alerta.codigoDemo') } catch { return false }
}

/** Busca centrada no GPS de quem consulta. A posição vai só na consulta; o servidor não a armazena. */
export function usePedidosComunitarios(posicao: Posicao | null, raioKm: number, municipio?: string | null) {
  const q = new URLSearchParams({ raioKm: String(raioKm) })
  if (posicao) { q.set('lat', posicao.latitude.toFixed(4)); q.set('lon', posicao.longitude.toFixed(4)) }
  else if (municipio) q.set('municipio', municipio)
  if (emDemonstracao()) q.set('incluirDemonstrativos', 'true')
  return useDados<RespostaComunidade>(`/api/comunidade/pedidos?${q}`, { intervaloMs: 30000 })
}

/** Polígono aproximando um círculo (para desenhar áreas e margens de precisão no mapa). */
export function circulo(lat: number, lon: number, raioM: number, passos = 64): [number, number][] {
  const pts: [number, number][] = []
  const dLat = raioM / 111_320
  const dLon = raioM / (111_320 * Math.cos((lat * Math.PI) / 180))
  for (let i = 0; i <= passos; i++) {
    const a = (i / passos) * 2 * Math.PI
    pts.push([lon + dLon * Math.cos(a), lat + dLat * Math.sin(a)])
  }
  return pts
}

/* ───────────── Transferência por QR (aparelho sem internet → aparelho próximo com internet) ───────────── */

export const VERSAO_QR = 'aea-qr/1'

const b64url = (bytes: Uint8Array) => btoa(String.fromCharCode(...bytes)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
const hex = (bytes: Uint8Array) => [...bytes].map((b) => b.toString(16).padStart(2, '0')).join('')
async function sha256(texto: string) { return new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(texto))) }

/** Mesmo cálculo de Ocorrencia.PseudonimoEmissor no servidor. Quem lê o QR não recebe o identificador real do aparelho. */
export async function pseudonimoEmissor(dispositivo: string): Promise<string> {
  return 'p:' + b64url((await sha256('alerta-em-acao/emissor/v1:' + dispositivo.trim())).slice(0, 16))
}

export interface PedidoQr {
  v: string; id: string; em: string; criado: string
  lat: number | null; lon: number | null; prec: number | null; lido: string | null
  m: Record<string, boolean>; pessoas: number | null; nec: string[]; comp: Compartilhamento | null; h: string; d?: boolean
}

const num = (v: number | null) => (v == null ? '' : String(Number(v.toFixed(6))))
const seg = (iso: string) => new Date(iso).toISOString().slice(0, 19) + 'Z'

/** Texto canônico — idêntico a ComunidadeEndpoints.CanonicoQr. */
export function canonicoQr(p: Omit<PedidoQr, 'h'>): string {
  const marcas = Object.entries(p.m).filter(([, v]) => v).map(([k]) => k).sort().join(',')
  const nec = [...p.nec].sort().join(',')
  const comp = p.comp?.autorizado ? `1${p.comp.incluirPessoas ? 1 : 0}${p.comp.incluirNecessidades ? 1 : 0}` : '0'
  return [p.v, p.id.toLowerCase(), p.em, seg(p.criado), num(p.lat), num(p.lon), num(p.prec), marcas, p.pessoas?.toString() ?? '', nec, comp, p.d ? 'd' : ''].join('|')
}

export async function hashQr(p: Omit<PedidoQr, 'h'>) { return hex(await sha256(canonicoQr(p))).slice(0, 32) }

const MARCAS = ['ilhado', 'aguaSubindo', 'correnteza', 'riscoDeslizamento', 'desmoronamento', 'barreiraComSinaisInstabilidade', 'acessoBloqueado',
  'equipamentoMedicoEssencial', 'mobilidadeReduzida', 'sozinho', 'riscoSegurancaPublica']

/**
 * Monta o QR a partir do que está salvo NESTE aparelho (pedido + complementos + compartilhamento ainda na fila).
 * Não inclui texto livre, idades, nomes ou telefone: só o necessário para a equipe agir.
 */
export async function montarPedidoQr(ocorrenciaId: string): Promise<PedidoQr | null> {
  const db = await bd()
  const itens = await db.getAll('fila')
  const pedido = itens.find((i) => i.id === ocorrenciaId && i.tipo === 'socorro')
  if (!pedido) return null
  const c = pedido.corpo as { criadaEm: string; latitude: number | null; longitude: number | null; precisaoMetros: number | null; localizacaoLidaEm: string | null }
  const m: Record<string, boolean> = {}
  let pessoas: number | null = null
  let nec: string[] = []
  for (const it of itens.filter((i) => i.referenciaId === ocorrenciaId && i.tipo === 'complemento').sort((a, b) => a.criadoEm.localeCompare(b.criadoEm))) {
    const d = it.corpo as Record<string, unknown>
    for (const k of MARCAS) if (d[k] === true) m[k] = true
    if (typeof d.quantidadePessoas === 'number') pessoas = d.quantidadePessoas
    if (Array.isArray(d.necessidades)) nec = d.necessidades as string[]
  }
  const comp = [...itens].reverse().find((i) => i.referenciaId === ocorrenciaId && i.tipo === 'compartilhamento')?.corpo as Compartilhamento | undefined
  const base: Omit<PedidoQr, 'h'> = {
    v: VERSAO_QR, id: ocorrenciaId, em: await pseudonimoEmissor(await dispositivoId()), criado: seg(c.criadaEm),
    // Arredondadas a 6 casas (~0,1 m) para que o texto canônico seja idêntico no aparelho e no servidor.
    lat: c.latitude == null ? null : Number(c.latitude.toFixed(6)), lon: c.longitude == null ? null : Number(c.longitude.toFixed(6)), prec: c.precisaoMetros == null ? null : Math.round(c.precisaoMetros), lido: c.localizacaoLidaEm,
    m, pessoas, nec, comp: comp ?? null, d: emDemonstracao() || undefined,
  }
  return { ...base, h: await hashQr(base) }
}

export function codificarQr(p: PedidoQr): string {
  return b64url(new TextEncoder().encode(JSON.stringify(p)))
}

export function decodificarQr(fragmento: string): PedidoQr | null {
  try {
    const t = fragmento.replace(/^#?p=/, '').replace(/-/g, '+').replace(/_/g, '/')
    const bin = atob(t + '='.repeat((4 - (t.length % 4)) % 4))
    const p = JSON.parse(new TextDecoder().decode(Uint8Array.from(bin, (ch) => ch.charCodeAt(0)))) as PedidoQr
    return p.v === VERSAO_QR && typeof p.id === 'string' ? p : null
  } catch { return null }
}

/** Endereço que a câmera de qualquer celular abre — funciona sem app instalado no aparelho que ajuda. */
export const urlRetransmissao = (p: PedidoQr) => `${location.origin}/retransmitir#p=${codificarQr(p)}`
/** Mesmo conteúdo, para o app nativo Alerta em Ação Nearby (se instalado no aparelho da família). */
export const urlAppNativo = (p: PedidoQr) => `alertaemacao://importar#p=${codificarQr(p)}`
