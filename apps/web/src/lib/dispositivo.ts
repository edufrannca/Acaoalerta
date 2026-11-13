import { openDB, type DBSchema, type IDBPDatabase } from 'idb'

export type TipoItemFila = 'socorro' | 'complemento' | 'situacao' | 'localizacao' | 'oferta' | 'oferta_pedido' | 'compartilhamento' | 'retransmissao'

/** Estado de transmissão verificável de cada item salvo no aparelho. */
export type EstadoTransmissao =
  | 'salvo_no_aparelho'   // persistido localmente, ainda não transmitido
  | 'transmitindo'
  | 'recebido_servidor'   // servidor respondeu 2xx (pedido armazenado no servidor)
  | 'recusado'            // servidor recusou (4xx): requer correção

export interface ItemFila {
  id: string              // mesmo id usado no servidor (idempotência)
  tipo: TipoItemFila
  caminho: string
  metodo: 'POST' | 'PATCH' | 'PUT'
  corpo: unknown
  criadoEm: string
  estado: EstadoTransmissao
  tentativas: number
  ultimaTentativaEm?: string
  ultimoErro?: string
  recebidoEm?: string
  resposta?: unknown
  referenciaId?: string   // ex.: id da ocorrência para complementos
}

interface Esquema extends DBSchema {
  fila: { key: string; value: ItemFila; indexes: { 'por-estado': EstadoTransmissao } }
  config: { key: string; value: { chave: string; valor: unknown } }
  cache: { key: string; value: { chave: string; valor: unknown; salvoEm: string } }
}

let _db: Promise<IDBPDatabase<Esquema>> | null = null

export function bd() {
  _db ??= openDB<Esquema>('alerta-em-acao', 1, {
    upgrade(db) {
      const f = db.createObjectStore('fila', { keyPath: 'id' })
      f.createIndex('por-estado', 'estado')
      db.createObjectStore('config', { keyPath: 'chave' })
      db.createObjectStore('cache', { keyPath: 'chave' })
    },
  })
  return _db
}

/** Apenas para testes. */
export function _reiniciarBd() { _db = null }

export function uuid(): string {
  const c = globalThis.crypto as Crypto
  if (typeof c.randomUUID === 'function') return c.randomUUID()
  const b = c.getRandomValues(new Uint8Array(16))
  b[6] = (b[6] & 0x0f) | 0x40; b[8] = (b[8] & 0x3f) | 0x80
  const h = [...b].map((x) => x.toString(16).padStart(2, '0')).join('')
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`
}

let _dispositivo: string | null = null

/** Identificador aleatório do aparelho (não é dado pessoal; serve para o dono acompanhar os próprios pedidos). */
export async function dispositivoId(): Promise<string> {
  if (_dispositivo) return _dispositivo
  try {
    const db = await bd()
    const r = await db.get('config', 'dispositivo')
    if (r) return (_dispositivo = String(r.valor))
    const novo = 'disp-' + uuid().replaceAll('-', '')
    await db.put('config', { chave: 'dispositivo', valor: novo })
    return (_dispositivo = novo)
  } catch {
    return (_dispositivo = 'disp-' + uuid().replaceAll('-', ''))
  }
}

export async function salvarCache(chave: string, valor: unknown) {
  try { await (await bd()).put('cache', { chave, valor, salvoEm: new Date().toISOString() }) } catch { /* ignore */ }
}
export async function lerCache<T>(chave: string): Promise<{ valor: T; salvoEm: string } | null> {
  try { const r = await (await bd()).get('cache', chave); return r ? { valor: r.valor as T, salvoEm: r.salvoEm } : null } catch { return null }
}
