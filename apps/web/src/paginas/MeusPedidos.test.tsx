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
    await enfileirar({ id: 'x', tipo: 'socorro', caminho: '/api/ocorrencias', corpo: {} })
    render(<MemoryRouter><MeusPedidos /></MemoryRouter>)
    expect(await screen.findByText('Pedido salvo neste aparelho. Ainda não transmitido.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Acompanhar pedido' })).toHaveAttribute('href', '/socorro/x')
  })
})

describe('Contatos de emergência', () => {
  it('oferece links telefônicos para 193, 199, 192 e 190', () => {
    render(<ContatosEmergencia />)
    for (const n of ['193', '199', '192', '190']) expect(screen.getByText(n).closest('a')).toHaveAttribute('href', `tel:${n}`)
    expect(screen.getByText(/não sabe se ela foi atendida/)).toBeInTheDocument()
  })
})
