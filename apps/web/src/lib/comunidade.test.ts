import { describe, expect, it } from 'vitest'
import { canonicoQr, circulo, codificarQr, decodificarQr, hashQr, pseudonimoEmissor, VERSAO_QR, type PedidoQr } from './comunidade'

// Mesmo vetor usado em backend/tests/Unitarios/VisaoComunitariaTestes.cs (paridade aparelho ↔ servidor).
const base: Omit<PedidoQr, 'h'> = {
  v: VERSAO_QR, id: '0193A5B0-1111-7222-8333-444455556666', em: 'p:abc', criado: '2026-11-13T12:34:56.789Z',
  lat: -8.0402, lon: -34.9301, prec: 18, lido: null, m: { ilhado: true, aguaSubindo: false, acessoBloqueado: true }, pessoas: 3,
  nec: ['transporte_assistido', 'agua'], comp: { autorizado: true, incluirPessoas: false, incluirNecessidades: true },
}

describe('QR de retransmissão', () => {
  it('gera o texto canônico esperado pelo servidor', () => {
    expect(canonicoQr(base)).toBe('aea-qr/1|0193a5b0-1111-7222-8333-444455556666|p:abc|2026-11-13T12:34:56Z|-8.0402|-34.9301|18|acessoBloqueado,ilhado|3|agua,transporte_assistido|101|')
  })

  it('detecta alteração do conteúdo e sobrevive à codificação na URL', async () => {
    const h = await hashQr(base)
    expect(h).toHaveLength(32)
    expect(await hashQr({ ...base, pessoas: 9 })).not.toBe(h)
    const p = { ...base, h }
    const volta = decodificarQr('#p=' + codificarQr(p))!
    expect(await hashQr(volta)).toBe(h)
    expect(volta.m).toEqual({ ilhado: true, acessoBloqueado: true })
    expect(codificarQr(p).length).toBeLessThan(codificarQr({ ...p, m: { ...p.m, x: false, y: false } }).length + 1)
    expect(decodificarQr('#p=lixo')).toBeNull()
  })

  it('pseudônimo do emissor não contém o identificador do aparelho', async () => {
    const p = await pseudonimoEmissor('aparelho-123456789012')
    expect(p).toMatch(/^p:[A-Za-z0-9_-]{22}$/)
    expect(p).not.toContain('aparelho')
    expect(await pseudonimoEmissor('aparelho-123456789012')).toBe(p)
  })

  it('círculo da área fecha o polígono', () => {
    const c = circulo(-8, -35, 600)
    expect(c[0]).toEqual(c[c.length - 1])
  })
})
