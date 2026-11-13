import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'

const handlers: Record<string, (d: unknown) => void> = {}
vi.mock('../lib/tempoReal', () => ({ useEvento: (e: string, h: (d: unknown) => void) => { handlers[e] = h } }))
const tocar = vi.fn(), parar = vi.fn()
vi.mock('../lib/alarme', async (orig) => ({ ...(await orig<typeof import('../lib/alarme')>()), tocarAlarme: (o: unknown) => tocar(o), pararAlarme: () => parar(), prepararAlarme: () => {} }))
const { AlarmeEmergencia } = await import('./AlarmeEmergencia')
const { deveAlarmar } = await import('../lib/alarme')

describe('Alarme de emergência', () => {
  afterEach(() => { cleanup(); tocar.mockClear(); parar.mockClear() })

  it('decide quais avisos tocam alarme', () => {
    expect(deveAlarmar({ tipo: 'familia', evento: 'pedido' })).toBe(true)
    expect(deveAlarmar({ tipo: 'familia', evento: 'complemento' })).toBe(false)
    expect(deveAlarmar({ tipo: 'preventivo', nivel: 'Critico' })).toBe(true)
    expect(deveAlarmar({ tipo: 'preventivo', nivel: 'Atencao' })).toBe(false)
    expect(deveAlarmar({ avisoId: 'a1' })).toBe(true)
    expect(deveAlarmar({})).toBe(false)
  })

  it('pedido de socorro na casa acompanhada: tela por escrito, alarme uma vez só, e para', () => {
    render(<MemoryRouter><AlarmeEmergencia /></MemoryRouter>)
    const n = { tipo: 'familia', evento: 'pedido', id: '1', titulo: 'Pedido de socorro na residência monitorada', mensagem: 'A residência "Casa da mãe" registrou um pedido de ajuda.', url: '/familia/pedidos/x' }
    act(() => handlers.notificacao(n))
    act(() => handlers.notificacao(n)) // repetido (push + tempo real)
    expect(screen.getByRole('alertdialog', { name: /Pedido de socorro/ })).toBeTruthy()
    expect(screen.getByText(/Casa da mãe/)).toBeTruthy()
    expect(screen.getByText(/193/, { selector: 'p' })).toBeTruthy()
    expect(tocar).toHaveBeenCalledTimes(1)
    expect(document.activeElement?.textContent).toBe('Ver detalhes')
    fireEvent.click(screen.getByRole('button', { name: 'Parar alarme' }))
    expect(parar).toHaveBeenCalled()
    expect(screen.queryByRole('alertdialog')).toBeNull()
  })

  it('atualização comum de familiar não toca alarme', () => {
    render(<MemoryRouter><AlarmeEmergencia /></MemoryRouter>)
    act(() => handlers.notificacao({ tipo: 'familia', evento: 'situacao', titulo: 'Atualização', url: '/x' }))
    expect(tocar).not.toHaveBeenCalled()
    expect(screen.queryByRole('alertdialog')).toBeNull()
  })
})
