import { beforeEach, describe, expect, it, vi } from 'vitest'
import { IDBFactory } from 'fake-indexeddb'
import { _reiniciarBd } from './dispositivo'
import { enfileirar, listar, obter, sincronizar, transmitir } from './filaOffline'

const resposta = (status: number, corpo: unknown = {}) => new Response(JSON.stringify(corpo), { status, headers: { 'Content-Type': 'application/json' } })
const esperar = () => new Promise((r) => setTimeout(r, 30))

describe('fila offline', () => {
  beforeEach(() => {
    globalThis.indexedDB = new IDBFactory()
    _reiniciarBd()
    vi.restoreAllMocks()
  })

  it('persiste no aparelho e NÃO marca como transmitido quando não há rede', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('Failed to fetch'))
    await enfileirar({ id: 'p1', tipo: 'socorro', caminho: '/api/ocorrencias', corpo: { id: 'p1' } })
    await esperar()
    const item = await obter('p1')
    expect(item?.estado).toBe('salvo_no_aparelho')
    expect(item?.ultimoErro).toMatch(/Sem conexão/)
    expect(item?.recebidoEm).toBeUndefined()
  })

  it('marca recebido somente após resposta 2xx do servidor', async () => {
    const f = vi.spyOn(globalThis, 'fetch').mockRejectedValueOnce(new TypeError('offline')).mockResolvedValue(resposta(201, { id: 'p2' }))
    await enfileirar({ id: 'p2', tipo: 'socorro', caminho: '/api/ocorrencias', corpo: { id: 'p2' } })
    await esperar()
    expect((await obter('p2'))?.estado).toBe('salvo_no_aparelho')
    expect(await sincronizar()).toBe(1)
    const item = await obter('p2')
    expect(item?.estado).toBe('recebido_servidor')
    expect(item?.recebidoEm).toBeDefined()
    // Reenvio usa o MESMO id (idempotência no servidor).
    const corpos = f.mock.calls.map((c) => JSON.parse(String((c[1] as RequestInit).body)).id)
    expect(new Set(corpos)).toEqual(new Set(['p2']))
  })

  it('erro 5xx mantém na fila; 4xx de validação marca recusado', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(resposta(503)).mockResolvedValueOnce(resposta(400, { title: 'inválido' }))
    await enfileirar({ id: 'a', tipo: 'situacao', caminho: '/api/situacoes', corpo: {} })
    await esperar()
    expect((await obter('a'))?.estado).toBe('salvo_no_aparelho')
    await transmitir('a')
    expect((await obter('a'))?.estado).toBe('recusado')
  })

  it('409 (conflito transitório) não é tratado como recusa definitiva', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(resposta(409))
    await enfileirar({ id: 'c', tipo: 'socorro', caminho: '/api/ocorrencias', corpo: {} })
    await esperar()
    expect((await obter('c'))?.estado).toBe('salvo_no_aparelho')
  })

  it('complemento aguarda o pedido principal chegar ao servidor', async () => {
    const f = vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('offline'))
    await enfileirar({ id: 'pai', tipo: 'socorro', caminho: '/api/ocorrencias', corpo: {} })
    await enfileirar({ id: 'filho', tipo: 'complemento', caminho: '/api/ocorrencias/pai/complemento', metodo: 'PATCH', corpo: {}, referenciaId: 'pai' })
    await esperar()
    expect(f.mock.calls.some((c) => String(c[0]).includes('complemento'))).toBe(false)
    f.mockImplementation(() => Promise.resolve(resposta(200)))
    await sincronizar()
    await esperar()
    expect((await obter('pai'))?.estado).toBe('recebido_servidor')
    expect((await obter('filho'))?.estado).toBe('recebido_servidor')
    const ordem = f.mock.calls.map((c) => String(c[0]))
    expect(ordem.indexOf('/api/ocorrencias')).toBeLessThan(ordem.indexOf('/api/ocorrencias/pai/complemento'))
  })

  it('envia identificador do dispositivo em todas as requisições', async () => {
    const f = vi.spyOn(globalThis, 'fetch').mockResolvedValue(resposta(201))
    await enfileirar({ id: 'd', tipo: 'socorro', caminho: '/api/ocorrencias', corpo: {} })
    await esperar()
    const cab = new Headers((f.mock.calls[0][1] as RequestInit).headers)
    expect(cab.get('X-Dispositivo-Id')).toMatch(/^disp-[0-9a-f]{32}$/)
    expect((await listar()).length).toBe(1)
  })
})
