import { bd, uuid } from './dispositivo'
import { enfileirar } from './filaOffline'
import type { Posicao } from './hooks'

/**
 * Ação imediata do botão "Preciso de socorro": gera id, registra horário e posição disponível,
 * persiste no aparelho e inicia a transmissão — sem formulário obrigatório.
 */
export async function iniciarSocorro(posicao: Posicao | null, avisoId?: string): Promise<string> {
  const id = uuid()
  await enfileirar({
    id, tipo: 'socorro', caminho: '/api/ocorrencias',
    corpo: {
      id, criadaEm: new Date().toISOString(),
      latitude: posicao?.latitude ?? null, longitude: posicao?.longitude ?? null,
      precisaoMetros: posicao?.precisao ?? null, localizacaoLidaEm: posicao?.lidaEm ?? null,
      avisoId: avisoId ?? null, incluirFamilia: true,
    },
  })
  return id
}

/** Se o pedido ainda não saiu do aparelho, incorpora a posição recém-obtida ao próprio pedido. */
export async function anexarPosicaoSePendente(id: string, p: Posicao): Promise<boolean> {
  const db = await bd()
  const item = await db.get('fila', id)
  if (!item || item.estado !== 'salvo_no_aparelho') return false
  const corpo = item.corpo as Record<string, unknown>
  if (corpo.latitude != null) return false
  item.corpo = { ...corpo, latitude: p.latitude, longitude: p.longitude, precisaoMetros: p.precisao, localizacaoLidaEm: p.lidaEm }
  await db.put('fila', item)
  return true
}

export async function complementar(ocorrenciaId: string, dados: Record<string, unknown>) {
  const id = uuid()
  await enfileirar({ id, tipo: 'complemento', caminho: `/api/ocorrencias/${ocorrenciaId}/complemento`, metodo: 'PATCH', corpo: dados, referenciaId: ocorrenciaId })
  return id
}
