import { api, ErroApi, ErroRede } from './api'
import { bd, type ItemFila, type TipoItemFila } from './dispositivo'

type Ouvinte = (itens: ItemFila[]) => void
const ouvintes = new Set<Ouvinte>()

async function notificar() {
  const itens = await listar()
  ouvintes.forEach((o) => o(itens))
}

export function observarFila(o: Ouvinte) {
  ouvintes.add(o)
  void listar().then(o)
  return () => { ouvintes.delete(o) }
}

export async function listar(): Promise<ItemFila[]> {
  const db = await bd()
  return (await db.getAll('fila')).sort((a, b) => b.criadoEm.localeCompare(a.criadoEm))
}

export async function obter(id: string) {
  return (await bd()).get('fila', id)
}

/**
 * 1) Persiste no IndexedDB ANTES de qualquer tentativa de rede.
 * 2) Tenta transmitir imediatamente. Se não houver rede, fica "salvo_no_aparelho".
 * O mesmo id é usado no servidor: reenvios nunca duplicam.
 */
export async function enfileirar(p: { id: string; tipo: TipoItemFila; caminho: string; metodo?: 'POST' | 'PATCH' | 'PUT'; corpo: unknown; referenciaId?: string }): Promise<ItemFila> {
  const item: ItemFila = {
    id: p.id, tipo: p.tipo, caminho: p.caminho, metodo: p.metodo ?? 'POST', corpo: p.corpo, referenciaId: p.referenciaId,
    criadoEm: new Date().toISOString(), estado: 'salvo_no_aparelho', tentativas: 0,
  }
  const db = await bd()
  await db.put('fila', item)
  await notificar()
  void transmitir(item.id)
  return item
}

const emAndamento = new Set<string>()

export async function transmitir(id: string): Promise<ItemFila | undefined> {
  if (emAndamento.has(id)) return obter(id)
  emAndamento.add(id)
  const db = await bd()
  try {
    const item = await db.get('fila', id)
    if (!item || item.estado === 'recebido_servidor' || item.estado === 'recusado') return item
    // Um complemento só pode ir depois do pedido principal.
    if (item.referenciaId) {
      const pai = await db.get('fila', item.referenciaId)
      if (pai && pai.estado !== 'recebido_servidor') return item
    }
    item.estado = 'transmitindo'
    item.tentativas++
    item.ultimaTentativaEm = new Date().toISOString()
    await db.put('fila', item)
    await notificar()
    try {
      const resp = await api(item.caminho, { method: item.metodo, json: item.corpo, timeoutMs: 12000 })
      item.estado = 'recebido_servidor'
      item.recebidoEm = new Date().toISOString()
      item.resposta = resp
      item.ultimoErro = undefined
    } catch (e) {
      if (e instanceof ErroApi && e.status >= 400 && e.status < 500 && e.status !== 408 && e.status !== 409 && e.status !== 429) {
        item.estado = 'recusado'
        item.ultimoErro = e.message
      } else {
        item.estado = 'salvo_no_aparelho'
        item.ultimoErro = e instanceof ErroRede ? 'Sem internet no momento' : e instanceof Error ? e.message : 'Falha'
      }
    }
    await db.put('fila', item)
    await notificar()
    if (item.estado === 'recebido_servidor') {
      // Libera dependentes (ex.: complementos aguardando o pedido).
      const dependentes = (await db.getAll('fila')).filter((x) => x.referenciaId === item.id && x.estado === 'salvo_no_aparelho')
      for (const d of dependentes) void transmitir(d.id)
    }
    return item
  } finally {
    emAndamento.delete(id)
  }
}

export async function sincronizar(): Promise<number> {
  const db = await bd()
  const pendentes = (await db.getAllFromIndex('fila', 'por-estado', 'salvo_no_aparelho'))
    .concat(await db.getAllFromIndex('fila', 'por-estado', 'transmitindo'))
    .sort((a, b) => a.criadoEm.localeCompare(b.criadoEm))
  let ok = 0
  for (const p of pendentes) {
    if ((await transmitir(p.id))?.estado === 'recebido_servidor') ok++
  }
  return ok
}

let iniciado = false
/** Sincroniza ao voltar a conexão e periodicamente (o app aberto não depende de Background Sync). */
export function iniciarSincronizacaoAutomatica() {
  if (iniciado || typeof window === 'undefined') return
  iniciado = true
  window.addEventListener('online', () => { void sincronizar() })
  setInterval(() => { if (navigator.onLine) void sincronizar() }, 15000)
  void sincronizar()
}

export const descricaoEstado: Record<ItemFila['estado'], string> = {
  salvo_no_aparelho: 'Salvo neste celular. Ainda não foi enviado.',
  transmitindo: 'Enviando…',
  recebido_servidor: 'Registrado!',
  recusado: 'Não foi possível registrar. Confira os dados e envie de novo.',
}

/** Mensagens ao cidadão sobre o pedido de socorro (o estado interno não muda). */
export const MENSAGENS_SOCORRO = {
  salvo: 'Seu pedido está salvo neste celular. Ainda não foi enviado.',
  enviando: 'Estamos enviando seu pedido de ajuda.',
  registrado: 'Seu pedido de ajuda foi registrado!',
  acompanhando: 'Agora estamos acompanhando o envio para a equipe responsável.',
  naoACaminho: 'Isso não significa que o socorro já está a caminho.',
  aguardandoEquipe: 'Estamos aguardando a confirmação da equipe.',
  equipeRecebeu: 'A equipe de teste recebeu seu pedido.',
  falha: 'Não conseguimos enviar seu pedido agora. Ele continua salvo neste celular.',
  internetVoltou: 'A internet voltou. Estamos tentando enviar seu pedido.',
  demonstracao: 'Demonstração: as equipes são simuladas e não representam atendimento real. Em emergência, ligue 193 ou 199.',
} as const

export function mensagemSocorro(estado: ItemFila['estado'], ultimoErro?: string | null, online = typeof navigator === 'undefined' || navigator.onLine) {
  if (estado === 'recebido_servidor') return MENSAGENS_SOCORRO.registrado
  if (estado === 'transmitindo') return MENSAGENS_SOCORRO.enviando
  if (estado === 'recusado') return descricaoEstado.recusado
  // Sem internet o pedido está apenas salvo; "falha" é quando houve conexão e o envio não deu certo.
  return ultimoErro && online ? MENSAGENS_SOCORRO.falha : MENSAGENS_SOCORRO.salvo
}
