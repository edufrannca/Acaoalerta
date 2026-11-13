import { dispositivoId } from './dispositivo'

const CHAVE_TOKEN = 'alerta.token'

/** Disparado quando a pessoa entra ou sai da conta (o tempo real reconecta com a nova identidade). */
export const EVENTO_TOKEN = 'alerta:token'

export function obterToken(): string | null {
  try { return localStorage.getItem(CHAVE_TOKEN) } catch { return null }
}
export function definirToken(t: string | null) {
  try { if (t) localStorage.setItem(CHAVE_TOKEN, t); else localStorage.removeItem(CHAVE_TOKEN) } catch { /* armazenamento indisponível */ }
  try { window.dispatchEvent(new Event(EVENTO_TOKEN)) } catch { /* fora do navegador */ }
}

export class ErroApi extends Error {
  status: number
  codigo?: string
  constructor(status: number, mensagem: string, codigo?: string) {
    super(mensagem)
    this.status = status
    this.codigo = codigo
  }
}

/** Erro de rede (sem resposta do servidor). Usado para distinguir "offline" de "recusado". */
export class ErroRede extends Error {}

export async function api<T = unknown>(caminho: string, opcoes: RequestInit & { json?: unknown; timeoutMs?: number } = {}): Promise<T> {
  const { json, timeoutMs = 15000, ...init } = opcoes
  const cab = new Headers(init.headers)
  cab.set('X-Dispositivo-Id', await dispositivoId())
  const token = obterToken()
  if (token) cab.set('Authorization', `Bearer ${token}`)
  if (json !== undefined) cab.set('Content-Type', 'application/json')
  const ctrl = new AbortController()
  const timer = setTimeout(() => ctrl.abort(), timeoutMs)
  let resp: Response
  try {
    resp = await fetch(caminho, { ...init, headers: cab, body: json !== undefined ? JSON.stringify(json) : init.body, signal: ctrl.signal })
  } catch (e) {
    throw new ErroRede(e instanceof Error ? e.message : 'Sem conexão')
  } finally {
    clearTimeout(timer)
  }
  if (resp.status === 401 && token) definirToken(null)
  const texto = await resp.text()
  const corpo = texto ? (() => { try { return JSON.parse(texto) } catch { return texto } })() : undefined
  if (!resp.ok) {
    const msg = typeof corpo === 'object' && corpo && 'title' in corpo ? String((corpo as { title: string }).title) : `Erro ${resp.status}`
    throw new ErroApi(resp.status, msg, typeof corpo === 'object' && corpo ? (corpo as { codigo?: string }).codigo : undefined)
  }
  return corpo as T
}

export const formatarHora = (iso?: string | null) =>
  iso ? new Date(iso).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', timeZone: 'America/Recife' }) : '—'

export const formatarRelativo = (iso?: string | null) => {
  if (!iso) return 'sem data'
  const min = Math.round((Date.now() - new Date(iso).getTime()) / 60000)
  if (min < 1) return 'agora'
  if (min < 60) return `há ${min} min`
  const h = Math.round(min / 60)
  if (h < 24) return `há ${h} h`
  return formatarHora(iso)
}
