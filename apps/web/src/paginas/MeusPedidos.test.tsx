import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { IDBFactory } from 'fake-indexeddb'
import { _reiniciarBd } from '../lib/dispositivo'
import { enfileirar } from '../lib/filaOffline'
import { MeusPedidos } from './MeusPedidos'
import { ContatosEmergencia } from '../componentes/Contatos'

describe('Meus pedidos', () => {
  beforeEach(() => { globalThis.indexedDB = new IDBFactory(); _reiniciarBd() })

  it('deixa explícito que o pedido está salvo no aparelho e ainda não foi transmitido', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('offline'))
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(false)
    await enfileirar({ id: 'x', tipo: 'socorro', caminho: '/api/ocorrencias', corpo: {} })
    render(<MemoryRouter><MeusPedidos /></MemoryRouter>)
    expect(await screen.findByText('Seu pedido está salvo neste celular. Ainda não foi enviado.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Acompanhar pedido' })).toHaveAttribute('href', '/socorro/x')
  })

  it('com internet, falha no envio diz que o pedido continua salvo', async () => {
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(new TypeError('falhou'))
    vi.spyOn(navigator, 'onLine', 'get').mockReturnValue(true)
    await enfileirar({ id: 'y', tipo: 'socorro', caminho: '/api/ocorrencias', corpo: {} })
    render(<MemoryRouter><MeusPedidos /></MemoryRouter>)
    expect(await screen.findByText('Não conseguimos enviar seu pedido agora. Ele continua salvo neste celular.')).toBeInTheDocument()
    expect(screen.queryByText(/servidor/)).toBeNull()
  })
})

describe('Contatos de emergência', () => {
  it('oferece links telefônicos para 193, 199, 192 e 190', () => {
    render(<ContatosEmergencia />)
    for (const n of ['193', '199', '192', '190']) expect(screen.getByText(n).closest('a')).toHaveAttribute('href', `tel:${n}`)
    expect(screen.getByText(/não sabe se ela foi atendida/)).toBeInTheDocument()
  })
})
